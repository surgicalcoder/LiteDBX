# `InvalidObjectIdPolicy` Plan

## Goal

Define clear, implementation-ready behavior for invalid legacy string values encountered during `string -> ObjectId` migrations.

This includes both:

- document identity migrations (`_id`)
- ordinary fields such as `CustomerId`, `ParentId`, or `LegacyRef.Id`

The policy must include a new `GenerateNewId` option because the migration process must be able to repair a broken collection instead of leaving it unreadable or partly migrated.

---

## Proposed enum

```csharp
public enum InvalidObjectIdPolicy
{
    Fail,
    SkipDocument,
    LeaveUnchanged,
    RemoveField,
    GenerateNewId
}
```

---

## Policy semantics by scenario

## 1. `_id` migration semantics

When migrating `_id`, the collection itself is at risk because typed access may fail until every affected document is repaired.

### Allowed policies for `_id`

- `Fail`
- `SkipDocument`
- `GenerateNewId`

### Disallowed or discouraged for `_id`

- `LeaveUnchanged` - leaves a broken collection behind
- `RemoveField` - would create invalid documents and should not be allowed

### Recommended default for `_id`

Use:

- `GenerateNewId` when the goal is guaranteed repair of the whole collection
- `Fail` when strict data integrity review is required before any swap occurs

### `GenerateNewId` behavior for `_id`

If `_id` is an invalid string:

1. generate `ObjectId.NewObjectId()`
2. assign it to `_id`
3. preserve the original invalid string in migration diagnostics
4. optionally write the original invalid value into a reserved audit field during rebuild, for example:
   - `__migration.legacyInvalidId`
   - or only journal it instead of persisting it in-document

In all cases, the old invalid id and the generated new id must be persisted durably in migration infrastructure storage so downstream external-reference repair can be done later.

#### Recommendation

Default to journaling the original invalid value rather than altering the public document shape unless the migration step explicitly enables in-document audit retention.

### Why `GenerateNewId` is acceptable for `_id`

- the alternative can be a permanently broken collection
- `_id` must exist and be valid
- a synthetic new id is better than an unqueryable row

### Major caveat

Changing `_id` can break external references.

Therefore, the migration report must capture an old-to-new id mapping for all regenerated ids, for example:

```text
collection=Settings, old="BAD-ID-123", new=507f1f77bcf86cd799439011
```

That mapping can later drive follow-up reference repair migrations.

### Durable mapping requirement

Do not keep these mappings only in-memory.

Recommended storage:

- `__migrations` stores summary counts and the `runId`
- `__migration_id_mappings` stores one row per regenerated id

Recommended mapping row fields:

- `migrationName`
- `runId`
- `collection`
- `selector`
- `sourceCollection`
- `shadowCollection`
- `backupCollection`
- `oldIdRaw`
- `oldIdType`
- `newObjectId`
- `policy`
- `reason`
- `documentOrdinal`
- `createdUtc`

---

## 2. Non-`_id` field migration semantics

For ordinary fields, the meaning of the value is domain-specific. Generating a fake new `ObjectId` can be useful, but it can also create false references.

### Allowed policies for normal fields

- `Fail`
- `SkipDocument`
- `LeaveUnchanged`
- `RemoveField`
- `GenerateNewId`

### Recommended default for normal fields

Use:

- `LeaveUnchanged` or `Fail` by default
- `GenerateNewId` only when the field is known to be an identity field that should always contain a unique `ObjectId`

### `GenerateNewId` behavior for normal fields

If the target field contains an invalid string:

1. generate `ObjectId.NewObjectId()`
2. replace the field value
3. optionally preserve the old value in:
   - migration report only, or
   - a companion field such as `CustomerId__legacy`

If the field participates in externally meaningful references, the same durable mapping approach used for `_id` can be optionally enabled for these non-id replacements as well.

### Recommendation

Do not make `GenerateNewId` the silent default for arbitrary reference fields. Require explicit opt-in.

---

## Decision table

| Scenario | Fail | SkipDocument | LeaveUnchanged | RemoveField | GenerateNewId |
|---|---:|---:|---:|---:|---:|
| `_id` string invalid during rebuild | Yes | Yes | No | No | Yes |
| top-level normal field invalid | Yes | Yes | Yes | Yes | Yes |
| nested normal field invalid | Yes | Yes | Yes | Yes | Yes |
| nullable/optional field | Yes | Yes | Yes | Yes | Yes |

---

## Rebuild executor behavior for `_id`

Because `_id` cannot be changed in-place, `_id` conversion uses rebuild/swap mode.

For each source document:

1. read raw document
2. inspect `_id`
3. if `_id` is `ObjectId`, keep it
4. if `_id` is a valid string, convert it
5. if `_id` is invalid:
   - `Fail` -> abort before swap
   - `SkipDocument` -> omit document and record it in report
   - `GenerateNewId` -> assign a fresh `ObjectId`, insert, and persist an old/new mapping row

Before swap:

- ensure no duplicate target ids
- ensure all inserts succeeded
- ensure policy outcomes are reflected in journal counts

---

## In-place executor behavior for normal fields

For each document:

1. locate target field by path
2. if field missing, no-op
3. if field already `ObjectId`, no-op
4. if field is valid string, replace with `ObjectId`
5. if invalid:
   - `Fail` -> abort migration
   - `SkipDocument` -> leave document unchanged and record skip
   - `LeaveUnchanged` -> keep field, continue, record unchanged-invalid count
   - `RemoveField` -> remove field
   - `GenerateNewId` -> replace with new `ObjectId`

---

## Reporting requirements

Every migration run should track:

- `InvalidValueCount`
- `SkippedDocumentCount`
- `GeneratedNewIdCount`
- `RemovedFieldCount`
- `UnchangedInvalidValueCount`
- sample old/new mappings for generated ids

For `_id` regeneration, store a full durable mapping set so downstream reference repair can be done safely even after the process ends.

---

## API proposal

### Fluent configuration

```csharp
.ForCollection("tenant_*", c => c
    .ConvertId()
    .FromStringToObjectId()
    .OnInvalidString(InvalidObjectIdPolicy.GenerateNewId))
```

```csharp
.ForCollection("*_orders", c => c
    .ConvertField("CustomerId")
    .FromStringToObjectId()
    .OnInvalidString(InvalidObjectIdPolicy.LeaveUnchanged))
```

### Optional explicit audit retention

```csharp
.ForCollection("Settings", c => c
    .ConvertId()
    .FromStringToObjectId()
    .OnInvalidString(InvalidObjectIdPolicy.GenerateNewId)
    .RecordLegacyInvalidValues())
```

---

## Tests required

### `_id`

- invalid string + `GenerateNewId` => document survives with new valid `_id`
- invalid string + `GenerateNewId` => durable row written to `__migration_id_mappings`
- invalid string + `Fail` => migration aborts, original collection unchanged
- invalid string + `SkipDocument` => document omitted, skip recorded, swap validation matches expected count

### normal field

- invalid string + `GenerateNewId` => field replaced with valid `ObjectId`
- invalid string + `RemoveField` => field removed
- invalid string + `LeaveUnchanged` => field preserved, migration completes

---

## Final recommendation

Treat `GenerateNewId` as:

- a first-class recovery policy for `_id`
- an explicit advanced option for non-id fields

And treat durable old->new mapping persistence as mandatory for `_id` regeneration so the migration framework helps with follow-up reference repair instead of only fixing local storage.

That gives the migration framework a way to guarantee collection repair without silently inventing reference values in places where the domain may not allow it.

