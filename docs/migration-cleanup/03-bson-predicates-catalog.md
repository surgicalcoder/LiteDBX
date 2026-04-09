# `BsonPredicates` Catalog Plan

## Goal

Make `BsonPredicates` a reusable cleanup vocabulary for migration steps instead of limiting it to empty arrays.

The predicate catalog should support:

- direct field cleanup
- nested-field cleanup
- conditional add operations
- conditional modify operations
- future recursive cleanup passes
- composition through `And`, `Or`, and `Not`

---

## Design goals

### 1. Small, obvious built-ins

Callers should be able to write:

```csharp
.RemoveFieldWhen("Tags", BsonPredicates.EmptyArray)
.RemoveFieldWhen("Notes", BsonPredicates.EmptyString)
.RemoveFieldWhen("Legacy.IsDeleted", BsonPredicates.FalseBoolean)
.AddFieldWhen("Metadata.Source", ctx => new BsonValue("migration"), when: BsonPredicates.Missing)
.ModifyFieldWhen("DisplayName", ctx => new BsonValue(ctx.Value.AsString.Trim()), when: BsonPredicates.IsString)
```

### 2. Composable building blocks

Support higher-order composition:

```csharp
BsonPredicates.Or(BsonPredicates.Null, BsonPredicates.EmptyString)
BsonPredicates.And(BsonPredicates.IsString, BsonPredicates.WhiteSpaceString)
BsonPredicates.Not(BsonPredicates.EmptyDocument)
```

### 3. Explicit handling for path-aware scenarios

Some conditions need document context rather than a raw value alone, especially if callers want `NullOrMissing` behavior. Plan for both:

- value predicates
- path/document-aware wrappers

The same predicate surface should drive:

- `RemoveFieldWhen(...)`
- `AddFieldWhen(...)`
- `ModifyFieldWhen(...)`

---

## Proposed predicate delegate shapes

### Minimal V1 form

```csharp
public delegate bool BsonValuePredicate(BsonValue value);
```

### Future-capable form

```csharp
public readonly record struct BsonPredicateContext(
    BsonDocument Root,
    string Path,
    bool Exists,
    BsonValue Value);

public delegate bool BsonPredicate(BsonPredicateContext context);
```

### Recommendation

Use the context-capable form internally, then expose simple helpers that ignore unused context.

That makes `NullOrMissing`, nested-path cleanup, and recursive cleanup easier later.

---

## Core built-in predicates

## Identity and emptiness

### `Null`
Matches BSON null.

### `Missing`
Matches absent field/path in context-aware execution.

Especially useful for `AddFieldWhen(...)` and `SetDefaultWhenMissing(...)`.

### `NullOrMissing`
Useful for cleanup passes or optional field removal.

### `EmptyArray`
Matches `BsonType.Array` with zero elements.

### `EmptyDocument`
Matches `BsonType.Document` with zero fields.

### `EmptyBinary`
Matches zero-length binary values.

---

## String-related

### `EmptyString`
Matches `""` only.

### `WhiteSpaceString`
Matches strings where `string.IsNullOrWhiteSpace(value.AsString)` is true.

### `NullOrWhiteSpaceString`
Equivalent to `Null OR WhiteSpaceString`.

### `TrimmedEmptyString`
Optional convenience if callers want to trim before testing instead of preserving raw whitespace semantics.

---

## Numeric and scalar defaults

### `ZeroInt32`
### `ZeroInt64`
### `ZeroNumber`
Matches any numeric BSON value equal to zero.

### `FalseBoolean`
Matches `false`.

### `MinValue`
### `MaxValue`
Useful for cleanup of sentinel values if the BSON model exposes them at runtime.

---

## Structured id/default sentinels

### `EmptyGuid`
Matches `Guid.Empty`.

### `EmptyObjectId`
Matches `ObjectId.Empty`.

### `Default(BsonValue defaultValue)`
Matches a caller-specified literal default.

### `AnyOfDefaults(params BsonValue[] defaults)`
Matches against a caller-specified default set.

---

## Higher-level cleanup predicates

### `NullLike`
Recommended definition:

- null
- missing
- empty string
- whitespace string

This should not automatically include empty arrays or empty documents unless explicitly documented.

### `StructurallyEmpty`
Recommended definition:

- empty array
- empty document
- empty binary

### `UselessValue`
Recommended default composition:

- `NullLike`
- `StructurallyEmpty`
- `EmptyGuid`
- `EmptyObjectId`
- `ZeroNumber`
- `FalseBoolean` only if the caller explicitly opts in

#### Important note
`false` and `0` are often meaningful business values. They should not be silently included in the most aggressive default cleanup unless the API name makes that clear.

Recommended split:

- `UselessValue` = conservative
- `UselessValueAggressive` = includes `ZeroNumber` and `FalseBoolean`

---

## Type predicates

Useful for composition and debugging:

- `IsString`
- `IsArray`
- `IsDocument`
- `IsNumber`
- `IsBoolean`
- `IsObjectId`
- `IsGuid`
- `IsNull`

---

## Composition helpers

### `And`

```csharp
BsonPredicates.And(BsonPredicates.IsString, BsonPredicates.WhiteSpaceString)
```

### `Or`

```csharp
BsonPredicates.Or(BsonPredicates.EmptyArray, BsonPredicates.EmptyDocument)
```

### `Not`

```csharp
BsonPredicates.Not(BsonPredicates.IsObjectId)
```

### `AnyOf`

Convenience wrapper for multiple `Or` operations.

### `AllOf`

Convenience wrapper for multiple `And` operations.

---

## Suggested starter catalog for V1

The following set is the minimum recommended initial implementation:

- `Null`
- `Missing`
- `NullOrMissing`
- `EmptyArray`
- `EmptyDocument`
- `EmptyString`
- `WhiteSpaceString`
- `NullOrWhiteSpaceString`
- `ZeroNumber`
- `FalseBoolean`
- `EmptyGuid`
- `EmptyObjectId`
- `Default(BsonValue value)`
- `AnyOfDefaults(params BsonValue[] values)`
- `NullLike`
- `StructurallyEmpty`
- `And`
- `Or`
- `Not`

These are sufficient not only for cleanup but also for the first add/modify operations. In particular:

- `Missing` and `NullOrMissing` are central to `AddFieldWhen(...)`
- `IsString`, `WhiteSpaceString`, and `Default(...)` are central to `ModifyFieldWhen(...)`

---

## Suggested additions for V2

- `UselessValue`
- `UselessValueAggressive`
- recursive cleanup helpers
- path-aware predicates that can inspect parents or siblings
- collection-level sweeps such as:

```csharp
.RemoveWhere(BsonPredicates.StructurallyEmpty, scope: CleanupScope.Recursive)
```

---

## API examples

### Targeted cleanup

```csharp
.ForCollection("Settings", c => c
    .RemoveFieldWhen("Tags", BsonPredicates.EmptyArray)
    .RemoveFieldWhen("Notes", BsonPredicates.WhiteSpaceString)
    .RemoveFieldWhen("Legacy.OwnerId", BsonPredicates.EmptyString))
```

### Custom defaults

```csharp
.ForCollection("Settings", c => c
    .RemoveFieldWhen("Flags.State", BsonPredicates.Default(new BsonValue("Unknown")))
    .RemoveFieldWhen("Stats.Count", BsonPredicates.ZeroNumber))
```

### Composition

```csharp
var cleanup = BsonPredicates.Or(
    BsonPredicates.NullLike,
    BsonPredicates.StructurallyEmpty);

.ForCollection("Settings", c => c
    .RemoveFieldWhen("Legacy.Payload", cleanup))
```

---

## Testing guidance

Test each built-in predicate individually and in combinations.

Required coverage:

- exact string emptiness vs whitespace behavior
- empty arrays/documents do not match unrelated types
- `Default(...)` handles scalar and structured values correctly
- `NullOrMissing` behaves correctly for nested absent paths
- composed predicates short-circuit and preserve expected semantics

---

## Recommendation

Make `BsonPredicates` slightly broader than the immediate empty-array request.

A strong initial catalog will make the migration framework useful for many one-off cleanup jobs without requiring custom delegates for every maintenance pass.

