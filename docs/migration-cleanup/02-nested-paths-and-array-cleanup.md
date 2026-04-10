# Nested Paths and Nested Cleanup Plan

## Goal

Support cleanup and type-conversion operations on more than just top-level fields.

This document is about BSON field-path traversal inside a document. Collection-name wildcard support for `ForCollection("*")`, prefix matches, suffix matches, and other selector patterns is a separate concern handled by the migration runner and collection selector layer.

The migration framework should reuse one shared path-navigation layer for:

- converting nested fields from `string` to `ObjectId`
- removing nested empty arrays
- removing nested empty strings, default values, and other predicate matches
- adding derived/default fields into nested documents
- modifying nested fields in place without rewriting sibling content
- optional pruning of now-empty parent containers

---

## Guiding principle

Do not build separate traversal logic for each migration type.

Instead, add a single reusable component, for example:

- `BsonPathNavigator`
- `BsonPathSegment`
- `BsonMutationResult`

That path layer should be shared by:

- `ConvertFieldTypeStep`
- `RemoveFieldWhenStep`
- `AddFieldWhenStep`
- `ModifyFieldWhenStep`
- future recursive cleanup passes

---

## V1 scope

V1 should be practical and low-risk.

### Supported path syntax in V1

- top-level field: `CustomerId`
- dotted nested document path: `Profile.CustomerId`
- deeper nested document path: `Settings.Legacy.OwnerId`

### Unsupported in V1

- array indexing: `Items[0].Id`
- wildcards: `Items[*].Id`
- recursive descent: `**.LegacyId`
- predicates inside paths
- path creation for missing parents

These limitations apply to BSON paths only. They do not restrict collection selectors such as `ForCollection("tenant_*")`.

### V1 traversal semantics

When walking `Profile.CustomerId`:

1. locate `Profile`
2. require it to be a document
3. locate `CustomerId` inside it
4. if any intermediate segment is missing or not a document, treat as no-op

This makes migration safe against heterogeneous document shapes.

### V1 mutation semantics

For remove operations:

- remove only the targeted field
- leave parent document intact by default
- optionally allow parent pruning when it becomes empty after child removal

For conversion operations:

- mutate only the targeted field
- do not create missing parents
- do not rewrite sibling content

For add operations:

- add only the targeted field
- do not overwrite an existing field by default
- do not create missing parent documents in v1
- if the parent path is missing or not a document, treat as no-op

For modify operations:

- replace only the targeted field
- if the field is missing, treat as no-op by default
- do not create missing parents
- do not rewrite sibling content

---

## V2 scope

V2 should expand the same path engine rather than replacing it.

### Current implemented baseline

The first V2 slice is now in place:

- fixed array index traversal such as `Items[0].LegacyId`
- direct indexed value paths such as `LegacyIds[0]`
- pruning through mixed document/array containers after indexed child removal

The next V2 slice is also now in place:

- wildcard array traversal such as `Items[*].LegacyId`
- wildcard application across existing array elements for field conversion and mutation
- concrete per-match path resolution for wildcard predicate contexts and execution
- paired wildcard path binding for sibling reference repair such as `Orders[*].Customer.$id` + `Orders[*].Customer.$ref`

The next recursive slice is now also in place:

- recursive descent such as `**.LegacyId`
- recursive traversal through nested documents and arrays
- concrete recursive match expansion for existing-target remove/modify/convert operations

This keeps the existing single-target mutation model intact while extending the shared navigator beyond dotted-document-only paths.

### Added syntax in V2

- array index: `Items[0].LegacyId`
- wildcard element selection: `Items[*].LegacyId`
- recursive traversal options for cleanup scans
- document-wide prune passes after field removal

### V2 nested cleanup examples

```csharp
.RemoveFieldWhen("Profile.Legacy.Tags", BsonPredicates.EmptyArray)
.RemoveFieldWhen("Orders[0].Legacy.Tags", BsonPredicates.EmptyArray, pruneEmptyParents: true)
.RemoveFieldWhen("Orders[*].LegacyId", BsonPredicates.WhiteSpaceString)
.PruneEmptyContainers()
```

At the moment, fixed-index, `[*]`, and a first recursive-descent slice are implemented. Document-wide cleanup passes remain roadmap items.

Current implementation note:

- fixed-index paths are implemented
- `[*]` paths are implemented for field add/set/modify/remove and `ConvertField(...)`
- `**` paths are implemented for existing-target `RemoveFieldWhen(...)`, `ModifyFieldWhen(...)`, and `ConvertField(...)`
- `RepairReference(...)` supports paired sibling wildcard binding when both paths share the same wildcard topology and parent path
- `AddFieldWhen(...)`, `SetFieldWhen(...)`, `RenameField(...)`, `CopyField(...)`, `MoveField(...)`, and recursive `RepairReference(...)` remain intentionally unsupported for recursive paths in this slice
- broader document-wide traversal/reporting remains future work

### V2 pruning behavior

If a child field is removed and its parent becomes empty:

- optionally remove the empty parent document
- optionally remove empty arrays discovered during recursive traversal
- optionally keep containers if the caller disables pruning

V2 can also add optional parent auto-creation for nested add/set operations, but that should not be the default in v1.

---

## Empty arrays in nested documents

## V1

Support targeted nested removal using dotted document paths only.

Example:

```csharp
.RemoveFieldWhen("Profile.Legacy.Tags", BsonPredicates.EmptyArray)
```

Behavior:

- if `Profile.Legacy.Tags` exists and is an empty array, remove only `Tags`
- if `Profile.Legacy` becomes empty afterward and pruning is enabled, remove `Legacy`
- if `Profile` becomes empty afterward and pruning is enabled, remove `Profile`

## V2

Support nested arrays and recursive cleanup passes.

Examples:

```csharp
.RemoveFieldWhen("Orders[*].Tags", BsonPredicates.EmptyArray)
.RemoveWhere(BsonPredicates.EmptyArray, scope: CleanupScope.Recursive)
```

This can reuse the same path traversal and pruning engine introduced for V1.

---

## Proposed path abstraction

### Parser result

A parsed path should produce segments such as:

- property segment: `Profile`
- property segment: `Legacy`
- property segment: `Tags`

Later V2 segments can add:

- array index segment: `[0]`
- wildcard segment: `[*]`

### Navigation API sketch

```csharp
var path = BsonPath.Parse("Profile.Legacy.Tags");
var result = BsonPathNavigator.TryGet(document, path);
```

Mutation helpers might look like:

```csharp
BsonPathNavigator.TryReplace(document, path, newValue, out var changed);
BsonPathNavigator.TryRemove(document, path, pruneEmptyParents: true, out var changed);
BsonPathNavigator.TryAdd(document, path, newValue, overwrite: false, out var changed);
```

---

## Cleanup semantics and parent pruning

### Recommendation

Add pruning as an option, not the hard-coded default.

Why:

- some consumers want only the exact field removed
- others want cleanup to cascade and erase now-empty containers

### Proposed options

```csharp
.RemoveFieldWhen("Profile.Legacy.Tags", BsonPredicates.EmptyArray)
.RemoveFieldWhen("Profile.Legacy.Tags", BsonPredicates.EmptyArray, pruneEmptyParents: true)
```

Or collection-level behavior:

```csharp
.ForCollection("Settings", c => c
    .PruneEmptyContainers()
    .RemoveFieldWhen("Profile.Legacy.Tags", BsonPredicates.EmptyArray))
```

---

## Error tolerance rules

Path traversal should not throw for mixed document shapes unless explicitly configured.

### Default behavior

- missing path => no-op
- intermediate scalar where document expected => no-op and record mismatch count
- intermediate array in V1 => no-op and record unsupported-path-shape count
- add-to-existing-target in non-overwrite mode => no-op and record skipped-existing-target count

### Strict mode option

Allow a stricter mode later if needed:

```csharp
.UseStrictPathResolution()
```

---

## Implementation stages

## Stage A - V1

Deliver:

- dotted path parsing for nested documents only
- top-level and nested document field removal
- top-level and nested document field conversion
- optional parent pruning
- test coverage for mixed-shape documents

## Stage B - V2

Deliver:

- array index support
- wildcard traversal
- recursive cleanup pass
- removal/pruning across arrays of documents
- full path-aware reporting

---

## Required tests

### V1

- top-level field removal works
- dotted nested field removal works
- dotted nested field conversion works
- missing path is safe no-op
- intermediate non-document is safe no-op
- parent pruning removes empty containers only when enabled

### V2

- array index path works
- wildcard path works across multiple child docs
- recursive cleanup removes nested empty arrays and empty strings
- pruning across arrays behaves predictably

Current coverage now includes the array-index and indexed-pruning portions of this list.

Coverage now also includes wildcard traversal for conversion, add/set semantics on existing parents, and pruning across wildcard-expanded array elements.

Coverage now also includes paired wildcard reference repair for guarded DbRef-like array elements.

Coverage now also includes recursive conversion and recursive pruning across nested document/array containers.

---

## Recommendation

Implement V1 and V2 on the same path-navigation abstraction from day one.

Even if V1 initially supports only dotted document paths, the internal model should be designed so array segments and wildcard traversal can be added without changing the public migration API.

