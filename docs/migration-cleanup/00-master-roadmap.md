# LiteDbX Migrations Master Roadmap

## Goal

Add a new `LiteDbX.Migrations` project that provides a fluent migration runner for raw `BsonDocument` collections.

The first two concrete migration scenarios are:

1. convert a field or document identity from `string` to `ObjectId` while preserving every other field
2. remove unwanted fields such as empty arrays

The design must also leave room for nested-field cleanup and additional predicate-driven maintenance.

It should also support conditional field enrichment and transformation, not only removal and type conversion.

It must also support applying the same migration definition to one collection, many collections, or collection-name patterns.

The implemented baseline now also includes whole-document mutation, durable reference repair, dry-run execution, backup retention/cleanup controls, and the first two V2 path-navigation slices for fixed array indices and `[*]` wildcard traversal.

---

## Core design decisions

### 1. Operate on raw `BsonDocument`

All migration steps should read and write raw BSON documents instead of typed entities.

Why:

- typed deserialization is already failing on legacy bad rows
- raw migration preserves unknown fields and legacy shape
- surgical updates are easier to reason about and verify

### 2. Support two execution modes

#### In-place update mode
Use for changes where `_id` remains unchanged.

Examples:

- `CustomerId: string -> ObjectId`
- removing empty arrays
- removing empty strings
- clearing default or useless fields

#### Rebuild-and-swap mode
Use for changes where `_id` itself changes.

Examples:

- `_id: string -> ObjectId`
- `_id: invalid string -> generated ObjectId`

This is required because the current engine update path treats `_id` as immutable.

### 3. Journal migrations

Use a dedicated collection such as `__migrations` to track:

- migration name/version
- start/completion time
- status
- collection name
- execution mode
- counts: scanned, changed, skipped, invalid, regenerated ids
- backup collection name when rebuild mode is used

### 4. Be idempotent wherever possible

A migration should safely no-op when a document is already in the desired state.

### 5. Expand `ForCollection(...)` into a collection selector

`ForCollection(...)` should accept either an exact collection name or a selector pattern.

Examples:

- `ForCollection("Settings")` - exact match
- `ForCollection("*")` - all user collections
- `ForCollection("tenant_*")` - prefix match
- `ForCollection("*_archive")` - suffix match
- `ForCollection("*_settings_*")` - general glob-style match

Recommended semantics:

- treat `*` as a multi-character wildcard in the collection name
- match against user collections only by default
- exclude `$` system collections and migration infrastructure collections such as `__migrations` and `__migration_id_mappings` unless explicitly opted in
- expand selectors deterministically from `GetCollectionNames()` before running steps
- process matched collections in stable ordinal order for predictable reporting

### 6. Persist generated id remaps durably

When `InvalidObjectIdPolicy.GenerateNewId` is used, the migration must persist old->new id mappings in durable storage, not only in an in-memory report.

Recommended storage:

- summary counts in `__migrations`
- full remap rows in a separate collection such as `__migration_id_mappings`

---

## Proposed public API

### Runner shape

```csharp
await db.Migrations()
    .UseJournal("__migrations")
    .UseIdMappingCollection("__migration_id_mappings")
    .Migration("2026-04-09-fix-legacy-id", m => m
        .ForCollection("tenant_*", c => c
            .ConvertId()
            .FromStringToObjectId()
            .OnInvalidString(InvalidObjectIdPolicy.GenerateNewId)))
    .Migration("2026-04-09-remove-empty-fields", m => m
        .ForCollection("*_settings", c => c
            .RemoveFieldWhen("Tags", BsonPredicates.EmptyArray)
            .RemoveFieldWhen("Notes", BsonPredicates.EmptyString)
            .AddFieldWhen("Metadata.Source", ctx => new BsonValue("migration"), when: BsonPredicates.Missing)
            .ModifyFieldWhen("DisplayName", ctx => new BsonValue(ctx.Value.AsString.Trim()), when: BsonPredicates.IsString)))
    .RunAsync();
```

### Fluent building blocks

```csharp
ForCollection("Settings")
ForCollection("*")
ForCollection("tenant_*")
ConvertId().FromStringToObjectId()
ConvertField("CustomerId").FromStringToObjectId()
RemoveFieldWhen("Tags", BsonPredicates.EmptyArray)
AddFieldWhen("Metadata.Source", ctx => new BsonValue("migration"), when: BsonPredicates.Missing)
ModifyFieldWhen("Name", ctx => new BsonValue(ctx.Value.AsString.Trim()), when: BsonPredicates.IsString)
RemoveFieldWhen("Path.To.Field", BsonPredicates.WhiteSpaceString)
RemoveFieldWhen("Flags.Legacy", BsonPredicates.Default(BsonValue.False))
```

See [`04-mutation-primitives.md`](./04-mutation-primitives.md) for the detailed design of these field mutation operations and related recommended APIs.

---

## Proposed project structure

### `LiteDbX.Migrations/`

- `MigrationRunner.cs`
- `MigrationBuilder.cs`
- `MigrationDefinition.cs`
- `MigrationContext.cs`
- `MigrationOptions.cs`
- `MigrationReport.cs`
- `CollectionSelector.cs`
- `CollectionMigrationBuilder.cs`
- `Steps/ConvertFieldTypeStep.cs`
- `Steps/ConvertIdStep.cs`
- `Steps/RemoveFieldWhenStep.cs`
- `Steps/AddFieldWhenStep.cs`
- `Steps/ModifyFieldWhenStep.cs`
- `Execution/InPlaceMigrationExecutor.cs`
- `Execution/RebuildMigrationExecutor.cs`
- `Execution/BsonPathNavigator.cs`
- `Predicates/BsonPredicates.cs`
- `Journal/MigrationJournal.cs`
- `Journal/MigrationJournalEntry.cs`
- `Journal/IdRemapLog.cs`
- `Journal/IdRemapEntry.cs`

### `LiteDbX.Migrations.Tests/`

- integration tests for each migration step
- edge-case tests for invalid ids and nested traversal
- idempotency tests
- index recreation tests for rebuild mode

---

## Execution model

## Collection selector expansion

1. resolve every `ForCollection(...)` selector against current user collections
2. exclude system/migration collections unless explicitly included
3. if the selector matches nothing, record an unmatched selector result in the report
4. execute the configured steps once per matched collection
5. aggregate per-collection results under the migration run report

## A. Convert non-`_id` field `string -> ObjectId`

1. stream raw documents
2. inspect target field
3. if field is already `ObjectId`, skip
4. if field is valid hex string, replace with `ObjectId`
5. if field is invalid, apply `InvalidObjectIdPolicy`
6. update only modified documents

## B. Convert `_id` `string -> ObjectId`

1. create shadow collection, e.g. `Settings__migrating`
2. read source documents as raw BSON
3. clone each document
4. convert `_id`
5. if invalid, apply `InvalidObjectIdPolicy.GenerateNewId` or configured policy
6. insert transformed document into shadow collection
7. recreate secondary indexes
8. validate counts and uniqueness
9. rename original to backup
10. rename shadow to original name
11. persist old->new mappings for every generated replacement id in `__migration_id_mappings`
12. record backup collection and mapping summary in the migration journal

## C. Remove fields by predicate

1. stream raw documents
2. locate field by path
3. if predicate matches, remove field
4. if removal leaves empty parent documents and configured pruning allows it, prune parents
5. update document in place

## D. Add fields by predicate and value factory

1. stream raw documents
2. locate the target field/path
3. evaluate the `when` predicate against shared mutation context
4. if predicate matches and target does not already exist, compute the new value
5. add only the targeted field
6. update document in place

## E. Modify fields by predicate and mutator

1. stream raw documents
2. locate the target field/path
3. if field exists and predicate matches, compute the replacement value
4. replace only the targeted field
5. preserve all unrelated fields
6. update document in place

---

## Invalid `ObjectId` policy summary

The enum should include:

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

See [`01-invalid-objectid-policy.md`](./01-invalid-objectid-policy.md) for detailed semantics.

Short guidance:

- `_id` migrations should support `Fail`, `SkipDocument`, and `GenerateNewId`
- `GenerateNewId` should be the recommended fallback for `_id` when the collection must be made writable again
- every `GenerateNewId` result must emit a durable old->new mapping row linked to the migration run id
- non-id fields may support all policies, but `GenerateNewId` should be explicit and not silent by default

---

## Collection selector semantics

`ForCollection(...)` should be treated as a selector API rather than a literal-only API.

### Supported matching in v1

- exact name: `Settings`
- all user collections: `*`
- prefix pattern: `tenant_*`
- suffix pattern: `*_archive`
- infix/glob pattern: `*_settings_*`

### Safety rules

- do not match `$` system collections by default
- do not match `__migrations` or `__migration_id_mappings` by default
- require explicit opt-in to include infrastructure/system collections
- store the original selector text and resolved collection list in the report

### Reporting requirements

For each selector, report:

- selector text
- matched collections
- unmatched selectors
- skipped collections
- per-collection success/failure summaries

---

## Nested-field scope

See [`02-nested-paths-and-array-cleanup.md`](./02-nested-paths-and-array-cleanup.md).

### V1

- top-level fields
- dotted paths through nested documents only, such as `Profile.CustomerId`
- no array indexing or wildcards
- reusable path traversal for conversion and cleanup

### V2

- fixed array indices: `Items[0].LegacyId` and `LegacyIds[0]`
- wildcard traversal: `Items[*].LegacyId`
- pruning nested empty arrays/documents after child removal
- optional document-wide cleanup passes using composed predicates

Current baseline note:

- fixed `[index]` traversal is implemented
- `[*]` traversal is implemented for field mutation/conversion operations over existing array matches
- paired wildcard `RepairReference(...)` is implemented for sibling-bound paths such as `Orders[*].Customer.$id` + `Orders[*].Customer.$ref`
- recursive descent such as `**.LegacyId` is implemented for existing-target remove/modify/convert operations and recursive add/set context expansion
- paired wildcard `RenameField(...)`, `CopyField(...)`, and `MoveField(...)` are implemented when source/target paths share the same wildcard topology and parent path
- recursive `RepairReference(...)` still remains intentionally unsupported

---

## Expanded cleanup predicate catalog

See [`03-bson-predicates-catalog.md`](./03-bson-predicates-catalog.md).

Minimum useful built-ins:

- `EmptyArray`
- `EmptyDocument`
- `EmptyString`
- `WhiteSpaceString`
- `Null`
- `NullOrMissing` (document/path aware helper)
- `ZeroNumber`
- `FalseBoolean`
- `EmptyBinary`
- `EmptyGuid`
- `EmptyObjectId`
- `Default(BsonValue value)`
- `NullLike`
- `UselessValue`
- combinators: `And`, `Or`, `Not`, `AnyOf`, `AllOf`

Predicates should also be reusable for add and modify operations, not only removal.

---

## Additional mutation primitives recommended

Beyond `RemoveFieldWhen(...)`, `AddFieldWhen(...)`, and `ModifyFieldWhen(...)`, the most useful next operations are:

- `SetFieldWhen(...)` for explicit overwrite semantics
- `RenameField(...)`
- `CopyField(...)`
- `MoveField(...)`
- `SetDefaultWhenMissing(...)`
- `RemoveDocumentWhen(...)`
- `InsertDocumentWhen(...)` for seed/reference data cases
- later: `ModifyDocumentWhen(...)` as a broader escape hatch

Recommended rule: keep `AddFieldWhen(...)` non-overwriting by default and reserve overwrite behavior for `SetFieldWhen(...)` or an explicit option.

---

## Index handling requirements

Rebuild mode must preserve secondary indexes.

Capture and replay:

- name
- expression
- unique flag

Do not recreate the primary `_id` index manually.

Recommended source for metadata:

- query index metadata from the engine/system collection layer already exposed by the repo
- recreate through the normal collection `EnsureIndex(...)` APIs on the shadow collection

---

## Durable id remap logging requirements

Whenever `GenerateNewId` is used for `_id`, persist a row to a dedicated collection such as `__migration_id_mappings`.

Recommended fields:

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
- `documentOrdinal` or another deterministic per-run reference
- `createdUtc`

The migration journal should store summary counts and the `runId` needed to join to the durable mapping rows.

---

## Safety requirements

### Dry run
Add a dry-run/report mode before destructive execution.

The report should include:

- documents scanned
- documents that would change
- invalid values encountered
- examples of bad values
- duplicate target id detection results
- selector expansion results
- generated id counts and a sample of old->new mappings

### Backups
For rebuild mode, keep the original collection as a backup until the caller explicitly removes it.

### Validation before swap
Require all of the following before renaming:

- no duplicate target `_id`
- target insert count matches source count minus intentionally skipped documents
- secondary indexes recreated successfully

---

## Recommended stage plan

## Stage 1 - project scaffolding and infrastructure

Deliver:

- new `LiteDbX.Migrations` and `LiteDbX.Migrations.Tests` projects
- solution updates
- journal types
- collection selector abstraction
- migration runner skeleton
- shared raw-document iteration helpers

## Stage 2 - in-place migration primitives

Deliver:

- `ConvertField(...).FromStringToObjectId()`
- `RemoveFieldWhen(...)`
- `AddFieldWhen(...)`
- `ModifyFieldWhen(...)`
- `BsonPredicates` initial catalog
- top-level field support
- dotted nested-document path support (v1)

## Stage 3 - rebuild-and-swap identity migration

Deliver:

- `ConvertId().FromStringToObjectId()`
- shadow collection creation
- index replay
- swap and backup flow
- `GenerateNewId` support for invalid `_id`
- durable old->new id remap persistence

## Stage 4 - nested cleanup v2

Deliver:

- array index traversal
- wildcard/recursive options
- parent pruning after child removal
- richer cleanup and mutation passes

## Stage 5 - polish and UX

Deliver:

- dry-run reporting
- preview invalid-value counts and capped samples for ObjectId conversion dry-runs
- rebuild validation summary reporting for source/target counts and planned index replay
- duplicate target `_id` preview counts and capped samples for rebuild dry-runs
- planned secondary-index replay detail reporting for rebuild dry-runs and applied rebuilds
- keep-latest-N backup cleanup retention for retained rebuild backups
- progress callbacks
- strict path resolution via `MigrationRunOptions.StrictPathResolution` and `MigrationRunner.UseStrictPathResolution()`
- collection-level insert reporting via `DocumentsInserted`
- higher-level mutation helpers such as `RenameField`, `CopyField`, `MoveField`, and `SetDefaultWhenMissing`
- optional shell integration or sample host
- end-to-end docs and examples

## Stage 6 - operational safety

Deliver:

- `MigrationRunOptions`
- dry-run execution with no writes, no journal entries, and no remap persistence
- backup retention policy for rebuild/swap migrations
- backup disposition metadata in reports
- duplicate target `_id` validation surfaced in preview and execution

---

## Test matrix

### Id conversion

- valid string `_id` converts to `ObjectId`
- invalid string `_id` fails when policy is `Fail`
- invalid string `_id` generates a new id when policy is `GenerateNewId`
- generated ids are persisted in `__migration_id_mappings` with old and new values
- unchanged documents preserve all other fields exactly
- index recreation succeeds after rebuild

### Field conversion

- top-level string field converts to `ObjectId`
- nested document field converts in v1
- invalid field string honors selected policy
- already-correct `ObjectId` field is a no-op

### Field mutation primitives

- `AddFieldWhen(...)` adds only when predicate matches and target is absent
- `ModifyFieldWhen(...)` updates only the targeted field and preserves all others
- non-overwriting add semantics remain idempotent across reruns
- nested document add/modify operations work in v1 for dotted document paths

### Cleanup predicates

- empty arrays removed
- empty strings removed
- whitespace strings removed
- default value predicates remove matching fields only
- parent pruning works as configured

### Idempotency

- rerunning the same migration yields zero changes after first success
- journal prevents duplicate applied versions unless explicitly overridden

### Collection selectors

- `ForCollection("*")` resolves all user collections but excludes system/migration collections by default
- prefix/suffix/infix selectors resolve deterministically
- unmatched selectors are reported without crashing the whole run unless strict mode is enabled

### Operational safety

- dry-run reports changes without mutating data or writing history/remap rows
- rebuild dry-run reports planned backup names/dispositions without creating backup/shadow collections
- backup retention policy behaves correctly after successful swaps

---

## Recommendation

Implement the migration package as a library first. Keep shell/CLI integration optional until the core engine is stable and tested.

