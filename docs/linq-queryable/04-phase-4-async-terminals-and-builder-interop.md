# Phase 4 — Async Terminals and Builder Interop

## Phase Goal

Make the LINQ provider executable using async terminals while preserving the native `Query()` fluent API as the canonical escape hatch.

## Existing Files To Study

- `LiteDbX/Client/Database/ILiteQueryable.cs`
- `LiteDbX/Client/Database/LiteQueryable.cs`
- `LiteDbX/Client/Database/LiteRepository.cs`
- `LiteDbX/Engine/Query/QueryOptimization.cs`
- `LiteDbX/Engine/Query/Structures/QueryPlan.cs`
- `LiteDbX/Engine/Query/QueryExecutor.cs`
- `LiteDbX/Engine/Query/Pipeline/QueryPipe.cs`

## Main Design Objective

A provider-backed `IQueryable<T>` should execute by lowering into the same native query path used today.

## Work Packages

### P4.1 — Define async terminal extensions

#### Recommended terminal set
- `ToListAsync`
- `ToArrayAsync`
- `FirstAsync`
- `FirstOrDefaultAsync`
- `SingleAsync`
- `SingleOrDefaultAsync`
- `AnyAsync`
- `CountAsync`
- `LongCountAsync`

#### Why extensions are recommended
They fit the async-only execution model without pretending that classic sync `IQueryable` materialization is universally supported.

#### Acceptance criteria
A documented async terminal surface that maps onto existing `LiteQueryable<T>` terminal behavior.

---

### P4.2 — Lower provider queries into the native query path

#### Goal
Execution should eventually route through the same path used by `collection.Query()`.

#### Recommended execution model
Queryable translation should produce either:

- a native `Query` object, or
- a native `LiteQueryable<T>` / equivalent builder state

which then executes through the existing engine and pipeline.

#### Acceptance criteria
A translated LINQ query uses the current optimizer and query pipeline instead of bypassing them.

---

### P4.3 — Preserve builder escape hatches

#### Goal
Keep the native builder available for advanced or unsupported scenarios.

#### Required behavior
Users should still be able to use:

- `collection.Query()`
- direct `BsonExpression` filters
- `GetPlan()`
- existing projection/grouping APIs

#### Acceptance criteria
Documentation and implementation both make it clear that the LINQ layer is additive, not replacing the native builder.

---

### P4.4 — Preserve query-plan visibility

#### Goal
Do not lose introspection when adding the LINQ layer.

#### Recommended capability
The translated query should be inspectable in terms of:

- final `Query`
- `ToSQL(...)` output
- explain-plan output where possible

#### Acceptance criteria
There is a documented way to inspect or debug provider-translated queries, even if some helpers stay internal at first.

## Deliverables

- async terminal plan and/or implementation scope
- execution-lowering plan into the native builder/`Query`
- documented escape hatches
- query-plan/debugging story

## Validation

- ensure async terminals map cleanly to existing `ILiteQueryableResult<T>` behavior
- verify translated queries execute through `QueryOptimization` and `QueryExecutor`
- verify provider-backed behavior matches native `Query()` behavior for MVP operators

## Suggested Test Focus

- `ToListAsync` parity
- `FirstAsync` / `SingleAsync` parity
- `AnyAsync` / `CountAsync` parity
- explain-plan / debug visibility where exposed
- clear failure path for unsupported sync execution

## Out of Scope

- full grouping support
- advanced aggregate operators beyond MVP scope
- provider support for arbitrary LINQ operators

## Exit Criteria

This phase is done when a provider-backed query can be composed with MVP operators and executed asynchronously through the existing LiteDbX query engine, with the native `Query()` API still available as a first-class alternative.

