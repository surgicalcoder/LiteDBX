# Phase 3 — MVP Operator Translation

## Phase Goal

Translate the highest-value LINQ operators into the existing LiteDbX query model.

This is the first phase where the provider becomes meaningfully useful.

## Existing Files To Study

- `LiteDbX/Client/Database/LiteQueryable.cs`
- `LiteDbX/Client/Database/Collections/Find.cs`
- `LiteDbX/Client/Mapper/BsonMapper.cs`
- `LiteDbX/Client/Mapper/Linq/LinqExpressionVisitor.cs`
- `LiteDbX/Client/Structures/Query.cs`
- `LiteDbX.Tests/Query/Where_Tests.cs`
- `LiteDbX.Tests/Query/OrderBy_Tests.cs`
- `LiteDbX.Tests/Query/Select_Tests.cs`

## MVP Operators

### Sequence-shaping operators
- `Where`
- `OrderBy`
- `OrderByDescending`
- `ThenBy`
- `ThenByDescending`
- `Skip`
- `Take`
- `Select`

### Operators intentionally not in this phase
- `GroupBy`
- `Join`
- `SelectMany`
- set operators
- nested subqueries
- custom provider-specific extensions unless needed for MVP

## Work Packages

### P3.1 — Translate `Where`

#### Target behavior
Map LINQ predicates onto the same predicate translation used by the native builder.

#### Reuse point
Use `BsonMapper.GetExpression(...)` for the predicate lambda body.

#### Acceptance criteria
`AsQueryable().Where(predicate)` should lower equivalently to `Query().Where(predicate)`.

---

### P3.2 — Translate ordering

#### Target behavior
Map LINQ ordering operators onto `Query.OrderBy` / native builder ordering semantics.

#### Operators
- `OrderBy`
- `OrderByDescending`
- `ThenBy`
- `ThenByDescending`

#### Reuse point
Use the same key-selector translation path currently used by `LiteQueryable<T>.OrderBy(...)`.

#### Acceptance criteria
Ordering produced by the queryable provider matches the order produced by the native fluent query builder.

---

### P3.3 — Translate paging

#### Target behavior
Map:

- `Skip` → offset
- `Take` → limit

#### Acceptance criteria
Provider output lowers to the same `Query.Offset` / `Query.Limit` values the native builder would produce.

---

### P3.4 — Translate simple `Select`

#### Target behavior
Support the same projection shapes that already work through the current query API.

#### Recommended MVP projection shapes
- scalar member projections
- anonymous/document-shaped projections already expressible via the current mapper visitor
- simple computed projections already supported by `LinqExpressionVisitor`

#### Caveat
Do not claim support for arbitrary client-evaluated projection logic.

#### Acceptance criteria
Provider-backed `Select` lowers to the same `Query.Select` semantics as `LiteQueryable<T>.Select(...)` for supported shapes.

---

### P3.5 — Add unsupported-shape diagnostics

#### Goal
Fail clearly when the provider sees operators or projection shapes outside MVP scope.

#### Acceptance criteria
Provider errors should identify:

- the unsupported LINQ method or pattern
- that the pattern is outside current LiteDbX LINQ support
- where users should fall back to the native `Query()` API if applicable

## Deliverables

- translation support for MVP sequence operators
- clear error path for unsupported shapes
- initial translation tests and/or parity tests

## Validation

- compare translated behavior to `collection.Query()` output
- compare result sets against in-memory LINQ where current tests already do this
- confirm projection behavior matches currently-supported mapper/query behavior

## Suggested Test Focus

- filter parity
- sort parity
- paging parity
- scalar projection parity
- anonymous projection parity where already supported
- unsupported operator diagnostics

## Out of Scope

- execution terminals
- `Include`
- grouping
- advanced aggregates
- public user documentation

## Exit Criteria

This phase is done when a provider-backed query can reliably compose and lower these shapes:

- `Where(...).ToQueryState()`
- `Where(...).OrderBy(...).ToQueryState()`
- `Where(...).OrderBy(...).Skip(...).Take(...).ToQueryState()`
- `Where(...).Select(...).ToQueryState()`

where the lowered state is equivalent to the native builder path.

