# Inheritance-Aware `BsonMapper` Conventions Plan

## Goal

Add a configurable, inheritance-aware mapping feature to `BsonMapper` so a consumer can define rules once for a base class and have them apply to every derived entity.

The original requirement was:

- a base class like `Entity`
- an inherited `Id` member
- configurable BSON storage type for that `Id` (for example, CLR `string` stored as BSON `ObjectId`)

The expanded requirement now also includes:

- **two inherited base members that should be ignored** for all descendants
- **one inherited base member that should use a custom serializer/deserializer** for all descendants

So the implementation should no longer be treated as only an `_id` feature. It should become a **base-type convention system** for inherited members, with `_id` support as one specialized convention.

---

## Summary of the revised design

### Recommended design direction

Instead of adding only a single method like `MapInheritedId<TBase>(...)`, introduce a mapper-level convention builder for a base type, for example:

```csharp
var mapper = new BsonMapper();

mapper.Inheritance<Entity>()
    .Id(x => x.Id, BsonType.ObjectId, autoId: true)
    .Ignore(x => x.InternalVersion)
    .Ignore(x => x.TransientState)
    .Serialize(
        x => x.PartitionKey,
        serialize: value => ...,
        deserialize: bson => ...);
```

This design is preferable because all three new requirements are fundamentally the same kind of feature:

- they target a member declared on a base type
- they apply to any descendant type
- they alter how `BuildEntityMapper(...)` produces a `MemberMapper`

That means the cleanest implementation is a shared inheritance-convention pipeline with three supported operations:

1. inherited id mapping and storage control
2. inherited ignore
3. inherited member codec (serializer/deserializer)

---

## Current behavior summary

After reviewing the code, the current mapper already has pieces we can reuse, but not a first-class API for this exact scenario.

### What already exists

#### 1. Inherited members are already discovered
`BsonMapper.GetEntityMapper(...)` and `BuildEntityMapper(...)` use `GetTypeMembers(...)`, which includes inherited public members.

That means base-class properties like `Entity.Id` are already visible when a derived type is mapped.

#### 2. Id convention already exists
`GetIdMember(...)` currently resolves ids by priority:

1. `[BsonId]`
2. `[Key]`
3. `Id`
4. `{TypeName}Id`

So an inherited `Id` property is already naturally discovered as an id candidate.

#### 3. Ignore already exists, but only through existing mechanisms
Ignored members currently come from:

- `[BsonIgnore]`
- `[NotMapped]`
- explicit fluent removal via `Entity<T>().Ignore(...)`
- manual low-level changes through `ResolveMember`

There is no built-in way to say:

> For every class inheriting from `Entity`, ignore these inherited members.

#### 4. Per-member custom serialization already exists
`MemberMapper` already supports:

- `Serialize`
- `Deserialize`

These are used by normal object serialization/deserialization.

So custom serialization for one inherited base member is already possible in principle, but only by mutating `MemberMapper` manually at mapping time.

#### 5. `_id` conversion is not consistently applied across all paths
This is the most important constraint.

Even though `MemberMapper.Serialize` / `Deserialize` exist, some critical paths bypass them today:

- constructor-based materialization in `GetTypeCtor(...)`
- generated-id write-back in `Client/Database/Collections/Insert.cs`
- LINQ constant/parameter serialization in `Client/Mapper/Linq/LinqExpressionVisitor.cs`
- DbRef `$id` serialization in `BsonMapper.cs`
- collection `AutoId` inference in `Client/Database/LiteCollection.cs`

So the implementation must not stop at mapper construction. It must propagate inherited member conventions through every path that relies on member semantics.

---

## Core design change caused by the new requirements

The new requirements change the implementation strategy in one major way:

### Before
A targeted `_id` feature would have been enough:

- register one inherited `Id` rule
- apply special serialization for `_id`

### Now
The feature should be generalized into a broader **inheritance-aware member convention system**.

That system must support, per base type:

- **Id member convention**
- **Ignore member conventions**
- **Custom member serialization conventions**

This is important because trying to bolt ignore and custom serializer behavior onto an id-only feature would make the API fragmented and the implementation harder to reason about.

---

## Recommended public API

## Option A: Preferred API

### `Inheritance<TBase>()` builder

Add a new mapper-level builder surface:

```csharp
public InheritedEntityBuilder<TBase> Inheritance<TBase>()
```

Proposed builder methods:

```csharp
Id<TMember>(Expression<Func<TBase, TMember>> member, BsonType storageType, bool autoId = true)
Ignore<TMember>(Expression<Func<TBase, TMember>> member)
Serialize<TMember>(
    Expression<Func<TBase, TMember>> member,
    Func<TMember, BsonMapper, BsonValue> serialize,
    Func<BsonValue, BsonMapper, TMember> deserialize)
```

Optional convenience overloads:

```csharp
Id(BsonType storageType, bool autoId = true) // assumes base member named Id
Serialize<TMember>(..., Func<TMember, BsonValue> serialize, Func<BsonValue, TMember> deserialize)
```

### Why this is the best API

Because it groups all base-type-wide member conventions in one place:

- easier to discover
- easier to document
- easier to validate for conflicts
- avoids a growing list of separate top-level methods on `BsonMapper`

---

## Option B: Acceptable but less clean

Separate mapper methods:

```csharp
MapInheritedId<TBase, TMember>(...)
IgnoreInheritedMember<TBase, TMember>(...)
MapInheritedSerializer<TBase, TMember>(...)
```

This would work, but it is less cohesive and makes conflict handling more scattered.

### Recommendation
Use **Option A**.

---

## Proposed convention model

Introduce internal convention descriptors stored by `BsonMapper`, likely keyed by:

- base type
- declared member
- convention kind

Suggested internal categories:

1. `InheritedIdConvention`
2. `InheritedIgnoreConvention`
3. `InheritedMemberSerializationConvention`

Each convention should include at least:

- target base type
- target member name or `MemberInfo`
- CLR member type
- conversion delegates when relevant
- storage type when relevant
- validation metadata

---

## Precedence and coexistence rules

These rules become more important now that multiple convention kinds can affect the same base type.

### Recommended precedence order

1. built-in member discovery and attributes (`[BsonId]`, `[BsonField]`, `[BsonIgnore]`, `[NotMapped]`, `[BsonRef]`)
2. inheritance conventions from `mapper.Inheritance<TBase>()`
3. `ResolveMember` callback
4. explicit per-entity fluent overrides via `Entity<T>()...`

### Why this order

- attributes/defaults establish the baseline
- inheritance conventions provide broad reusable rules
- `ResolveMember` remains a low-level escape hatch
- explicit per-entity fluent mapping should still be strongest for one concrete type

### Conflict rules

These should fail fast during configuration or mapper build:

#### Invalid combinations on the same member
- `Ignore(...)` + `Id(...)`
- `Ignore(...)` + `Serialize(...)`

Reason: once a member is ignored, there is no meaningful serialization/id behavior left to apply.

#### Valid combinations
- `Id(...)` on one member + `Ignore(...)` on other members
- `Id(...)` on one member + `Serialize(...)` on another member
- multiple `Ignore(...)` calls on different members

#### Same convention registered twice
Choose one of these policies:

- either reject duplicate registrations for the same base/member/convention kind
- or let the most recent overwrite the old one

### Recommendation
Reject duplicates unless there is a strong reason to support replacement. It makes behavior easier to reason about.

---

## Behavior rules for the three supported conventions

## 1. Inherited `Id` storage convention

Example:

- base class `Entity` declares `string Id`
- derived entity `Customer : Entity`
- store `_id` as BSON `ObjectId`
- expose CLR `Id` as string

### Required behavior

- member maps to `_id`
- stored BSON type follows configured `storageType`
- CLR member type remains unchanged
- if `autoId = true`, generated `_id` values must be converted back into CLR member type

### Auto-id restrictions
If `autoId = true`, only allow storage types that the engine can generate safely:

- `ObjectId`
- `Guid`
- `Int32`
- `Int64`

If a custom serializer is used for an id member with `autoId = true`, validation should ensure the generated BSON type still aligns with one of those auto-id categories.

### Important note
The id convention is effectively a specialized member codec plus `_id` field assignment and auto-id semantics.

---

## 2. Inherited ignore conventions

Example:

- base class `Entity` declares `InternalVersion`
- base class `Entity` declares `TransientState`
- all descendants should omit them from BSON entirely

### Required behavior

For any derived mapped type:

- those inherited members should not appear in `EntityMapper.Members`
  - or should have `FieldName = null` / `IsIgnore = true` consistently, depending on implementation choice
- they must not serialize
- they must not deserialize
- they must not appear in query member resolution as document fields

### Recommendation
Prefer removing or marking them ignored during mapper build, before downstream use.

That minimizes accidental leakage into serialization, deserialization, or query translation.

---

## 3. Inherited custom member serializer/deserializer

Example:

- base class `Entity` declares `PartitionKey`
- every descendant should serialize that member using a custom codec

### Required behavior

For every derived mapped type:

- the inherited member uses configured serialize/deserialize delegates
- the field name remains whatever the normal mapping rules produce, unless separately overridden later
- ctor hydration and regular property hydration should be consistent with the custom codec when applicable

### Recommendation
Keep this feature member-scoped, not type-global.

Do **not** use `RegisterType<T>()` for this scenario, because that would affect every member of that CLR type across the model.

---

## Affected files and symbols

### Primary mapper construction files
- `LiteDbX/Client/Mapper/BsonMapper.cs`
- `LiteDbX/Client/Mapper/BsonMapper.GetEntityMapper.cs`
- `LiteDbX/Client/Mapper/MemberMapper.cs`
- `LiteDbX/Client/Mapper/EntityBuilder.cs`
- `LiteDbX/Client/Mapper/EntityMapper.cs`

### Serialization / deserialization flow
- `LiteDbX/Client/Mapper/BsonMapper.Serialize.cs`
- `LiteDbX/Client/Mapper/BsonMapper.Deserialize.cs`

### Query translation / LINQ
- `LiteDbX/Client/Mapper/Linq/LinqExpressionVisitor.cs`

### Collection id behavior
- `LiteDbX/Client/Database/LiteCollection.cs`
- `LiteDbX/Client/Database/Collections/Insert.cs`
- `LiteDbX/Client/Database/Collections/Upsert.cs`
- possibly `LiteDbX/Client/Database/Collections/Update.cs`

### DbRef behavior
- `LiteDbX/Client/Mapper/BsonMapper.cs` (`RegisterDbRefItem`, `RegisterDbRefList`)

### Tests to add/extend
- `LiteDbX.Tests/Mapper/CustomMapping_Tests.cs`
- `LiteDbX.Tests/Mapper/CustomMappingCtor_Tests.cs`
- `LiteDbX.Tests/Mapper/LinqBsonExpression_Tests.cs`
- `LiteDbX.Tests/Database/...` existing id/auto-id coverage
- likely a new file such as `LiteDbX.Tests/Mapper/InheritedMemberConventions_Tests.cs`

---

## Implementation plan

## Phase 1 — Introduce a general inheritance-convention model

### 1. Add mapper-internal storage for inheritance conventions
In `BsonMapper`, add storage for base-type member conventions.

This should support:

- one id convention per base type/member
- zero or more ignored members per base type
- zero or more custom member codecs per base type

### 2. Add a public builder API
Add `Inheritance<TBase>()` on `BsonMapper`.

Add a new builder type, for example:

- `InheritedEntityBuilder<TBase>`

Builder methods:

- `Id(...)`
- `Ignore(...)`
- `Serialize(...)`

### 3. Validate configuration early
At registration time, validate:

- expressions point to direct members
- member belongs to or is inherited from `TBase`
- no conflicting ignore/id/serialize definitions exist
- `autoId` is valid for the configured storage type

---

## Phase 2 — Apply conventions during mapper construction

### 4. Integrate conventions into `BuildEntityMapper(...)`
When building a concrete `EntityMapper` for a derived type:

- detect which registered base-type conventions apply
- match inherited members on the concrete type
- apply ignore conventions
- apply custom member serializers/deserializers
- apply id storage convention

### 5. Decide the order inside `BuildEntityMapper(...)`
Recommended internal order:

1. discover members
2. skip `[BsonIgnore]` / `[NotMapped]`
3. compute normal field name
4. create `MemberMapper`
5. apply inheritance conventions
6. invoke `ResolveMember`
7. finalize inclusion into `mapper.Members`

This preserves existing behavior while giving inheritance conventions a formal place.

### 6. Handle ignored members consistently
If a member is matched by an inherited ignore convention, ensure it is not added to `mapper.Members`.

That is cleaner than adding it and hoping later paths skip it.

---

## Phase 3 — Extend `MemberMapper` metadata

### 7. Add storage/conversion metadata to `MemberMapper`
`MemberMapper` currently has:

- `DataType`
- `FieldName`
- `Serialize`
- `Deserialize`
- `AutoId`

To support inheritance conventions robustly, add metadata for things like:

- configured storage BSON type for ids
- whether this member was configured by inheritance convention
- maybe a small helper for converting CLR <-> stored BSON consistently

This is especially important for `_id`, because collection logic currently infers behavior only from CLR member type.

---

## Phase 4 — Centralize member-aware conversion helpers

### 8. Introduce shared member conversion helpers
Create shared internal logic so the same conversion is reused in all affected paths.

Examples:

- inherited `string Id` stored as `ObjectId`
- inherited custom serializer for another base member

That helper should support:

- serialize CLR member value to BSON according to member rules
- deserialize BSON value back to CLR according to member rules

### 9. Use the helper in normal object serialization
In `BsonMapper.Serialize.cs`, normal object serialization already checks `member.Serialize`.

The new inheritance conventions should either:

- populate `member.Serialize` / `member.Deserialize`
- or use a centralized metadata-based helper the serialize path calls

### Recommendation
Use `MemberMapper.Serialize` / `Deserialize` for member-specific convention behavior, but back them with shared helper logic to avoid duplicated conversion code.

---

## Phase 5 — Fix constructor materialization

### 10. Make ctor hydration member-aware
Current `GetTypeCtor(...)` materializes ctor parameters by field name and parameter type, then calls plain `Deserialize(type, value)`.

That is insufficient when:

- `_id` is stored as `ObjectId` but ctor expects `string id`
- a custom inherited member codec must be applied

### Plan
Change ctor argument materialization so that if a ctor parameter maps to a known `MemberMapper`, its member-aware deserializer is used.

This is required for both:

- inherited id storage conversion
- inherited custom serializer/deserializer

---

## Phase 6 — Fix collection id semantics

### 11. Update `LiteCollection<T>` auto-id inference
Current collection logic infers `AutoId` from `_id.DataType`.

That breaks when:

- CLR `Id` is `string`
- stored `_id` is `ObjectId`

### Plan
Teach `LiteCollection<T>` to infer `AutoId` from configured id storage type when present, otherwise fall back to current CLR-type-based logic.

### 12. Update empty-id detection in `Insert.cs`
`RemoveDocId(...)` currently checks BSON values against the inferred `AutoId` type.

That logic must remain correct when:

- CLR member is string
- serialized BSON id is `ObjectId`

Also define how to treat:

- `null`
- empty string
- invalid string when storage type is `ObjectId`

### Recommendation
For string-to-`ObjectId` id storage:

- `null` or empty string + `autoId: true` => remove `_id` so engine generates one
- non-empty invalid string => fail fast

### 13. Fix generated-id write-back
After insert/upsert, if the engine generated `_id`, the code currently writes back `id.RawValue`.

That is wrong for CLR/storage mismatches.

### Plan
When writing `_id` back to the entity, use the member-aware id deserializer so:

- stored `ObjectId` becomes CLR string
- stored `Guid` becomes CLR string if such a mapping is ever supported

This affects at least:

- `Insert(T entity, ...)`
- bulk insert path via `GetBsonDocs(...)`
- potentially upsert paths depending on generated-id handling

### 14. Include `Upsert.cs` in the implementation review
Even though `Upsert(BsonValue id, T entity, ...)` accepts explicit BSON id input, the collection-level behavior still needs review so id semantics stay consistent with the new inherited id feature.

---

## Phase 7 — Fix DbRef `$id` semantics

### 15. Use referenced entity id-member conversion in DbRef serialization
`RegisterDbRefItem(...)` and `RegisterDbRefList(...)` currently serialize `$id` using the runtime id type directly.

That is incorrect for inherited id storage rules such as:

- CLR `string Id`
- BSON `ObjectId` `$id`

### Plan
Serialize and deserialize `$id` using the referenced entity’s id `MemberMapper` rules, not just `id.GetType()`.

This keeps DbRef aligned with `_id` storage conventions.

---

## Phase 8 — Fix LINQ/query parameter serialization

### 16. Make LINQ parameter emission member-aware
Current `LinqExpressionVisitor.VisitConstant(...)` serializes parameters mostly from runtime type.

That means this query can fail silently if `_id` is stored as `ObjectId`:

```csharp
x => x.Id == someStringId
```

because the constant may be emitted as a BSON string instead of BSON `ObjectId`.

### Plan
When the visitor is resolving a comparison against a mapped member, it should use that member’s serialization rules for the constant/parameter side.

This is particularly important for:

- inherited `_id` storage conventions
- inherited custom member serializers if the member appears in predicates

### Scope choice
At minimum, fix `_id` and other direct mapped member comparisons.

If practical, support all member-aware constant serialization where the compared side is a known mapped member.

---

## Phase 9 — Tests

## New dedicated test file
Add a focused test suite, likely:

- `LiteDbX.Tests/Mapper/InheritedMemberConventions_Tests.cs`

## Test groups

### A. Inherited id storage tests
1. Base `Entity` with `string Id`, derived types inherit it.
2. Configure inherited id storage as `ObjectId`.
3. Serialize derived instance.
4. Assert `_id` is BSON `ObjectId`.
5. Deserialize document.
6. Assert CLR `Id` is the hex string.

### B. Auto-id generation tests
1. `Id = null` and `Id = ""`.
2. Insert entity.
3. Assert `_id` generated as BSON `ObjectId`.
4. Assert entity receives generated hex string.
5. Repeat for bulk insert.

### C. Inherited ignore tests
1. Base class defines two members to ignore.
2. Derived type adds its own properties.
3. Serialize derived instance.
4. Assert ignored members are absent.
5. Assert other properties remain.
6. Assert mapper member list does not expose ignored members.

### D. Inherited custom serializer tests
1. Base class defines a third member with custom serializer.
2. Derived type round-trips through document.
3. Assert serialized BSON shape matches custom codec.
4. Assert deserialized CLR member restores correctly.

### E. Constructor materialization tests
1. Derived type uses ctor-based hydration.
2. `_id` stored as BSON `ObjectId`.
3. ctor expects `string id`.
4. assert object hydrates correctly.
5. also test ctor parameter for the inherited custom-serialized member.

### F. LINQ tests
1. Query by inherited `Id` string against stored `ObjectId`.
2. Query by inherited custom-serialized member.
3. Ensure generated BsonExpression parameters use member-aware BSON values.

### G. DbRef tests
1. Referenced entity inherits configured base id rule.
2. DbRef `$id` stores BSON `ObjectId`.
3. Round-trip works.

### H. Isolation tests
1. Convention affects descendants of configured base type.
2. Unrelated classes with `Id` or same member names are unaffected.

### I. Conflict/validation tests
1. registering `Ignore` and `Id` for same member throws
2. registering `Ignore` and `Serialize` for same member throws
3. invalid `ObjectId` string throws clearly
4. late registration after mapper cache is built behaves according to chosen policy

---

## Key design decisions to make before implementation

## 1. Registration timing and entity-mapper cache
`BsonMapper` caches `EntityMapper` instances.

### Options
- **Option A:** require inheritance conventions to be registered before first use of any affected mapped type
- **Option B:** invalidate or patch cached mappers when conventions are added later

### Recommendation
Use **Option A** initially.

If someone registers an inheritance convention after an affected type has already been mapped, fail fast with a clear error.

That keeps the first implementation much safer.

---

## 2. Should inherited custom serializers affect LINQ constants?

### Recommendation
Yes, when the query is comparing against a known mapped member.

Otherwise read behavior and write behavior can drift apart.

---

## 3. How broad should the inherited serializer API be?

### Recommendation
Keep it member-scoped and symmetric:

- require both serialize and deserialize delegates
- support mapper-aware overloads

This avoids one-way or ambiguous conversions.

---

## 4. Hidden or overridden base members
Derived types may hide a base property with `new`.

### Recommendation
Resolve conventions against the actual mapped member selected for the concrete type, but validate that the configured member identity is unambiguous.

If ambiguous, fail fast.

---

## 5. Ignore representation
There are two implementation styles:

- remove the member from `EntityMapper.Members`
- keep it but mark ignored

### Recommendation
Prefer not adding ignored members to `EntityMapper.Members`.

This simplifies downstream behavior.

---

## 6. Should `Id(...)` internally be implemented as a special serializer?

### Recommendation
Partly yes.

Conceptually, inherited id storage is:

- `_id` field assignment
- auto-id metadata
- special member codec

So it should reuse the same member conversion infrastructure as `Serialize(...)`, while still preserving `_id`-specific behaviors.

---

## Proposed rollout strategy

### Step 1
Implement the inheritance builder and convention storage.

### Step 2
Apply conventions in `BuildEntityMapper(...)`.

### Step 3
Fix `_id` collection semantics and generated-id write-back.

### Step 4
Fix ctor hydration.

### Step 5
Fix LINQ parameter/member-aware serialization.

### Step 6
Fix DbRef `$id`.

### Step 7
Add and run targeted test coverage.

This ordering reduces risk because `_id` support is the most correctness-sensitive part.

---

## Final recommendation

Because the requirement now spans:

- inherited `Id` storage customization
- inherited ignored members
- inherited custom member serialization

this should be implemented as a **general inheritance-aware member convention API** in `BsonMapper`, not as a narrow one-off `_id` extension.

### Final recommended surface

```csharp
mapper.Inheritance<Entity>()
    .Id(x => x.Id, BsonType.ObjectId, autoId: true)
    .Ignore(x => x.InternalVersion)
    .Ignore(x => x.TransientState)
    .Serialize(x => x.PartitionKey, serialize: ..., deserialize: ...);
```

### Why
This approach:

- keeps all base-type-wide configuration in one place
- composes the three requirements cleanly
- avoids global type-side effects
- makes conflict handling explicit
- allows the `_id` feature to share the same member-conversion infrastructure as the custom serialized inherited member

---

## Concrete impact versus the original plan

Compared to the original id-only plan, the implementation changes in these ways:

1. **API expands** from a single inherited-id method to a full inheritance builder.
2. **Convention storage generalizes** from one id rule to multiple member-rule kinds.
3. **Mapper build logic becomes rule-driven** instead of handling `_id` as a special isolated case.
4. **LINQ support matters for more than `_id`** because custom serialized inherited members may also appear in predicates.
5. **Conflict validation becomes mandatory** because ignore and custom serialization can now target overlapping inherited members.
6. **Tests expand significantly** to verify that all three rule types coexist correctly on the same base class.

---

## Deliverable summary

If implemented, the feature should let a consumer configure a base class once and have all derived entities inherit these mapper rules safely:

- inherited id storage type control
- inherited ignored members
- inherited custom member serialization/deserialization

with correct behavior across:

- document serialization
- document deserialization
- constructor materialization
- insert/bulk insert/upsert id handling
- LINQ query translation
- DbRef references

