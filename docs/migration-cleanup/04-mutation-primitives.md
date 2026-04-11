# Mutation Primitives Plan

## Goal

Broaden `LiteDbX.Migrations` from a cleanup-only tool into a general-purpose document migration library.

In addition to:

- `ConvertField(...).FromStringToObjectId()`
- `ConvertId().FromStringToObjectId()`
- `RemoveFieldWhen(...)`

add first-class support for:

- `AddFieldWhen(...)`
- `ModifyFieldWhen(...)`

and define the next small set of mutation primitives that make the library broadly useful without forcing consumers into custom one-off transforms too early.

---

## Design principle

All mutation operations should execute against raw `BsonDocument` values and share the same foundational pieces:

- collection selector resolution
- BSON path traversal
- predicate evaluation
- mutation reporting
- dry-run support
- idempotent no-op behavior when a document already satisfies the target state

That keeps the API coherent and avoids a separate execution model for each operation.

---

## Proposed fluent surface

### Existing operations

```csharp
ConvertId().FromStringToObjectId()
ConvertField("CustomerId").FromStringToObjectId()
RemoveFieldWhen("Tags", BsonPredicates.EmptyArray)
```

### New operations

```csharp
AddFieldWhen("CreatedBy", ctx => new BsonValue("migration"), when: BsonPredicates.Missing)
ModifyFieldWhen("Name", value => new BsonValue(value.AsString.Trim()), when: BsonPredicates.WhiteSpaceString)
InsertDocumentWhen(new BsonDocument { ["_id"] = 42, ["Name"] = "seed" })
```

### Recommended refined examples

```csharp
.ForCollection("tenant_*", c => c
    .AddFieldWhen("Metadata.Source", ctx => new BsonValue("legacy-import"), when: BsonPredicates.Missing)
    .ModifyFieldWhen("Notes", ctx => new BsonValue(ctx.Value.AsString.Trim()), when: BsonPredicates.IsString)
    .RemoveFieldWhen("Tags", BsonPredicates.EmptyArray))
```

---

## Delegate shapes

### Predicate context

Use one shared context model so predicates and mutation calculators can reason about the same information.

```csharp
public readonly record struct BsonPredicateContext(
    BsonDocument Root,
    string Path,
    bool Exists,
    BsonValue Value,
    string Collection,
    string MigrationName);
```

### Value factory / mutator delegates

```csharp
public delegate BsonValue BsonValueFactory(BsonPredicateContext context);
public delegate BsonValue BsonValueMutator(BsonPredicateContext context);
```

This lets `AddFieldWhen(...)` and `ModifyFieldWhen(...)` share the same execution pipeline while still exposing intent-specific APIs.

---

## `AddFieldWhen(...)`

## Intent

Add a field when a condition matches and compute the new value lazily from context.

### Recommended semantics

`AddFieldWhen(...)` should mean:

- add the target field only if the `when` predicate matches
- do not overwrite an existing target value by default
- if the target path already exists, treat it as a no-op unless an explicit overwrite option is enabled

### Why this default matters

Callers usually expect “add” to behave like “set if missing,” not “replace.”

If overwrite is desired, that should be a separate operation or an explicit option.

### Suggested API

```csharp
AddFieldWhen(string path, BsonValueFactory factory, BsonPredicate when)
AddFieldWhen(string path, BsonValue value, BsonPredicate when)
```

Optional explicit behavior:

```csharp
AddFieldWhen("Metadata.Source", ctx => new BsonValue("legacy"), when: BsonPredicates.Missing)
    .IfTargetMissingOnly()
```

or:

```csharp
SetFieldWhen("Metadata.Source", ctx => new BsonValue("legacy"), when: BsonPredicates.Missing)
```

### Current implementation baseline

- supports top-level, dotted nested, indexed, wildcard, and recursive-descent paths
- `FieldMutationOptions.CreateParents` enables nested parent auto-creation for add/set operations
- `FieldMutationOptions.WriteMode` supports `MissingOnly`, `ExistingOnly`, `NullOrMissing`, and `Overwrite`
- if parent path is missing and parent creation is disabled, the operation is a safe no-op by default or a strict-path failure when strict resolution is enabled

### Notes

- non-recursive exact-path writes still preserve idempotent no-op behavior when the target already has the desired value
- recursive add/set works by expanding concrete match contexts and applying the same exact-target write helpers on the resulting paths

---

## `ModifyFieldWhen(...)`

## Intent

Conditionally transform an existing field while preserving the rest of the document.

### Suggested API

```csharp
ModifyFieldWhen(string path, BsonValueMutator mutator, BsonPredicate when)
```

### Semantics

- if the target path does not exist, no-op by default
- if the predicate matches, calculate a replacement value
- replace only the targeted field
- preserve the rest of the document unchanged

### Typical examples

```csharp
ModifyFieldWhen("Name", ctx => new BsonValue(ctx.Value.AsString.Trim()), when: BsonPredicates.IsString)
ModifyFieldWhen("Flags.Status", ctx => new BsonValue("Active"), when: BsonPredicates.Default(new BsonValue("Unknown")))
```

### Recommended v1 rules

- top-level and dotted nested document paths only
- no array traversal yet
- no parent creation
- if mutator returns the same value, treat as no-op

---

## Relationship to `RemoveFieldWhen(...)`

These three operations should feel like a family:

- `AddFieldWhen(...)`
- `ModifyFieldWhen(...)`
- `RemoveFieldWhen(...)`

All three should:

- take a path
- use the same context model
- support dry-run reporting
- participate in the same per-document mutation summary

---

## Additional core operations recommended

To make the library comprehensive without becoming too generic too early, the highest-value next additions are:

### 1. `SetFieldWhen(...)`

Use when overwrite is allowed and explicit.

Why:

- avoids ambiguity in `AddFieldWhen(...)`
- clearer than overloading `Add` with overwrite semantics

### 2. `RenameField(...)`

Move a value from one path/name to another while preserving the same value.

Examples:

```csharp
RenameField("OldName", "NewName")
RenameField("Legacy.OwnerId", "OwnerId")
```

### 3. `CopyField(...)`

Copy a value to another field without removing the source.

### 4. `MoveField(...)`

Copy then remove source if successful.

### 5. `SetDefaultWhenMissing(...)`

A convenience wrapper over `AddFieldWhen(...)` for the common “supply default if absent” case.

### 6. `RemoveDocumentWhen(...)`

Delete whole documents matching a predicate.

Useful for one-off cleanup migrations when field-level mutation is not enough.

### 7. `InsertDocumentWhen(...)`

Insert a document at collection scope for seed/reference-data cases.

Supported forms now include:

```csharp
InsertDocumentWhen(new BsonDocument { ["_id"] = 42, ["Name"] = "seed" })
InsertDocumentWhen(ctx => new BsonDocument { ["_id"] = 42, ["Name"] = ctx.CollectionName })
```

Default behavior is idempotent by `_id`: if the candidate `_id` already exists, insertion is skipped.

### 8. `ModifyDocumentWhen(...)`

A more advanced escape hatch for full-document transforms once the primitive surface is in place.

This should be added later than path-based operations because it is more powerful and easier to misuse.

---

## Prioritized roadmap for mutation operations

### High priority for v1/v1.5

- `AddFieldWhen(...)`
- `ModifyFieldWhen(...)`
- `SetFieldWhen(...)`
- `RenameField(...)`
- `CopyField(...)`
- `MoveField(...)`
- `SetDefaultWhenMissing(...)`

### Medium priority

- `RemoveDocumentWhen(...)`
- collection-level validation/precondition rules

### Later / advanced

- `ModifyDocumentWhen(...)`
- batch cross-collection reference repair using the id remap log
- recursive mutation sweeps

---

## Execution semantics recommendations

### No-op rules

- `AddFieldWhen(...)` => no-op when target exists, unless overwrite is explicitly allowed
- `ModifyFieldWhen(...)` => no-op when target missing or predicate false
- `RemoveFieldWhen(...)` => no-op when target missing or predicate false

### Reporting

Track separately:

- `AddedFieldCount`
- `ModifiedFieldCount`
- `RemovedFieldCount`
- `SkippedExistingFieldCount`
- `MissingPathCount`
- `PredicateMismatchCount`

### Dry run

Each mutation primitive should be able to produce a preview summary without writing changes.

---

## V1 and V2 scope

### V1

- top-level and dotted nested document paths
- no arrays or wildcards in BSON field paths
- no parent auto-creation
- explicit overwrite semantics only through dedicated operations

### V2

- array indices and wildcards
- parent auto-creation options
- recursive mutation passes
- richer document-level transforms

Current implementation note:

- indexed and wildcard add/set are implemented
- recursive add/set is implemented
- paired wildcard `RenameField(...)`, `CopyField(...)`, and `MoveField(...)` are implemented for aligned source/target paths
- recursive `RepairReference(...)` remains intentionally unsupported

---

## Test guidance

### `AddFieldWhen(...)`

- adds field when target missing and predicate matches
- does not overwrite existing field by default
- respects nested document path v1 rules
- no-op when parent path missing

### `ModifyFieldWhen(...)`

- modifies field when predicate matches
- no-op when field missing
- preserves all unrelated fields
- supports nested dotted paths in v1

### Related core operations

- `RenameField(...)` preserves value and removes old field
- `CopyField(...)` preserves both fields
- `MoveField(...)` behaves atomically from the migration’s point of view
- `SetDefaultWhenMissing(...)` is idempotent

---

## Recommendation

Yes, `AddFieldWhen(...)` and `ModifyFieldWhen(...)` are worth adding.

To make the library truly useful, I also recommend planning for this core mutation set:

- `AddFieldWhen(...)`
- `ModifyFieldWhen(...)`
- `RemoveFieldWhen(...)`
- `SetFieldWhen(...)`
- `RenameField(...)`
- `CopyField(...)`
- `MoveField(...)`
- `SetDefaultWhenMissing(...)`
- `ConvertField(...).FromStringToObjectId()`
- `ConvertId().FromStringToObjectId()`

That gives you a coherent, composable migration library before you need broader document-level escape hatches.

