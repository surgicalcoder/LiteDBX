# Phase 2 — Queryable Provider and State Model

## Phase Goal

Introduce the provider-side architecture needed to parse `Queryable` method chains without disturbing the current query engine.

This phase is about shape and responsibilities, not about broad operator support yet.

## Existing Files To Study

- `LiteDbX/Client/Database/Collections/Find.cs`
- `LiteDbX/Client/Database/ILiteCollection.cs`
- `LiteDbX/Client/Database/ILiteQueryable.cs`
- `LiteDbX/Client/Database/LiteQueryable.cs`
- `LiteDbX/Client/Mapper/BsonMapper.cs`
- `LiteDbX/Client/Mapper/Linq/LinqExpressionVisitor.cs`
- `LiteDbX/Client/Structures/Query.cs`

## Main Design Objective

Create a dedicated LINQ-provider layer that translates `System.Linq.Queryable` expression trees into the existing LiteDbX query model.

## Work Packages

### P2.1 — Define the provider/root types

#### Goal
Introduce the types that represent a provider-backed LINQ query.

#### Recommended components
- a provider type, e.g. `LiteDbXQueryProvider`
- a queryable root/wrapper type, e.g. `LiteDbXQueryable<T>`
- optional internal executor/helper type if separation improves clarity

#### Important note
Avoid naming collisions with the existing `LiteQueryable<T>` fluent builder. Keep the new queryable facade clearly distinct.

#### Acceptance criteria
- one documented provider type responsibility list
- one documented queryable root responsibility list
- clear separation from `LiteQueryable<T>`

---

### P2.2 — Define the translation boundary

#### Goal
Keep method-chain translation separate from lambda/body translation.

#### Recommended rule
- `Queryable.Where`, `Queryable.Select`, `Queryable.OrderBy`, etc. are parsed by a provider-side translator
- lambda bodies continue to flow through `BsonMapper.GetExpression(...)` / `LinqExpressionVisitor`

#### Why this matters
`LinqExpressionVisitor` is already good at translating expressions like `x => x.Age > 10`, but it is not the right place to interpret full queryable method chains.

#### Acceptance criteria
A clear written boundary between:

- method-chain translation
- leaf lambda translation
- execution lowering

---

### P2.3 — Define the internal state model

#### Goal
Represent the partially-translated query in a way that is safe, inspectable, and composable.

#### Recommended shape
Use an internal translation state or query specification that can capture:

- source collection name
- root entity type
- current result type
- filters
- ordering
- skip/take
- projection
- includes
- grouping metadata
- selected terminal operator
- transaction context
- flags for grouped/scalar/document projections

#### Why not mutate `Query` directly from the start?
Because method-chain parsing is easier to reason about using a normalized intermediate model, and the current fluent builder already shows shared-state risk around `_query` mutation.

#### Acceptance criteria
- written list of state fields
- rule for when state becomes a concrete `Query`
- rule for how to avoid cross-query contamination

---

### P2.4 — Decide mutability/cloning strategy

#### Goal
Prevent one queryable chain from corrupting another.

#### Risk anchor
`LiteQueryable<T>` currently mutates a shared `_query`, and aggregate methods temporarily replace `Select` and then restore it. A provider that reuses mutable query instances carelessly will become fragile.

#### Recommended direction
Use either:

- immutable translation state, or
- copy-on-write snapshots until final lowering into `Query`

#### Acceptance criteria
- documented mutability policy
- documented cloning/snapshot rules
- no ambiguity about reuse of translated queries

## Deliverables

- provider/root architecture plan
- translation-boundary rules
- internal state-model definition
- mutability/cloning decision

## Validation

- trace a hypothetical chain `Where -> OrderBy -> Select -> CountAsync`
- verify each stage has a clear owner
- verify the design still lowers into the existing `Query()` / engine path

## Out of Scope

- full operator support
- terminal execution implementation
- grouping implementation
- user-facing docs beyond architecture notes

## Exit Criteria

This phase is done when another implementer can explain:

1. which type owns expression-tree parsing
2. which type owns lambda-to-`BsonExpression` translation
3. how a partially-built LINQ query is stored safely
4. how the final state becomes a native LiteDbX query

