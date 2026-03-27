# Phase 1 — Public Surface and Contracts

## Phase Goal

Define exactly what LINQ / `IQueryable<T>` means in LiteDbX before building it.

This phase should settle the public API shape, async/sync policy, initial operator scope, and key non-goals.

## Why This Phase Comes First

Without a contract, later implementation work will drift into:

- accidental replacement of the current fluent API
- accidental sync-over-async execution
- overcommitment to unsupported LINQ semantics
- avoidable rewrites of provider internals

## Existing Files To Study

- `LiteDbX/Client/Database/ILiteCollection.cs`
- `LiteDbX/Client/Database/ILiteQueryable.cs`
- `LiteDbX/Client/Database/LiteQueryable.cs`
- `LiteDbX/Client/Database/LiteRepository.cs`
- `docs/ASYNC_ONLY_REDESIGN_PLAN.md`
- `docs/async-redesign/phase-1-decisions.md`

## Work Packages

### P1.1 — Choose the public entrypoint

#### Decision to make
How users get an `IQueryable<T>` root.

#### Recommended direction
Add a separate adapter-style entrypoint such as:

- `ILiteCollection<T>.AsQueryable()`
- possibly `ILiteCollection<T>.AsQueryable(ILiteTransaction transaction)`
- optionally a repository convenience entrypoint

#### Why this is recommended
The current `LiteQueryable<T>` is already a native fluent builder with explicit async terminals. Making it itself satisfy all `IQueryable<T>` expectations will blur responsibilities and create pressure for synchronous execution semantics.

#### Non-goal
Do not replace `Query()` with `AsQueryable()`.

#### Acceptance criteria
- one documented primary queryable entrypoint
- a short rationale for why it is separate from `Query()`
- a note on transaction-aware queryable creation

---

### P1.2 — Define sync vs async execution policy

#### Decision to make
What happens when callers attempt sync LINQ materializers or sync enumeration.

#### Recommended direction
Treat `IQueryable<T>` as a **sync composition surface** and expose **async execution** through provider-specific async terminals.

#### Recommended policy
- supported: async terminal methods/extensions
- unsupported: implicit sync materialization for provider-backed queries
- failure mode: clear exception with guidance to use async terminals

#### Why this is recommended
It matches the current async-only direction in `ILiteQueryableResult<T>` and avoids hidden sync-over-async.

#### Acceptance criteria
- documented sync terminal policy
- documented async terminal policy
- documented reason the project does not promise classic sync LINQ execution semantics

---

### P1.3 — Lock the supported operator matrix

#### MVP operators
- `Where`
- `Select`
- `OrderBy`
- `OrderByDescending`
- `ThenBy`
- `ThenByDescending`
- `Skip`
- `Take`
- async terminals for first/count/exists/materialization

#### Deferred operators
- `Include` as provider-specific extension/interceptor
- scalar aggregates beyond count/exists
- grouped aggregate projections

#### Explicitly out of initial scope
- `Join`
- `GroupJoin`
- `SelectMany`
- nested queryable subqueries
- set operators
- full LINQ `GroupBy` semantics returning `IGrouping<TKey, TElement>`

#### Acceptance criteria
A table mapping each operator to one of:

- supported in MVP
- deferred to later milestone
- unsupported / out of scope

---

### P1.4 — Write the architecture guardrails

#### Guardrails to lock
1. LINQ is an adapter over the existing query model.
2. `Query` remains the canonical structured query object.
3. `LiteQueryable<T>` remains the native escape hatch and advanced builder.
4. `BsonMapper.GetExpression(...)` and `LinqExpressionVisitor` remain lambda translators, not full queryable providers.
5. Unsupported LINQ constructs should fail clearly.

#### Acceptance criteria
A concise guardrail list copied into the master roadmap and prompt docs.

## Deliverables

- written public-surface decision
- sync/async execution decision
- supported-operator matrix
- guardrails and non-goals

## Validation

- compare decisions against `ILiteQueryable<T>` and `ILiteQueryableResult<T>`
- ensure the contract does not require replacing `Query()`
- ensure the contract respects the async-only redesign direction

## Out of Scope

- implementing provider types
- adding operator translation
- changing execution code
- changing tests beyond documenting intended additions

## Exit Criteria

This phase is done when another implementer can answer these questions unambiguously:

1. How do users start a LINQ query?
2. Is sync execution supported?
3. Which operators are in MVP?
4. What remains first-class in the existing native query API?

