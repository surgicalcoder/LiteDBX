# Phase 5 — Grouping, Aggregates, and Advanced Translation

## Phase Goal

Expand the LINQ provider beyond the MVP while staying within the limits of the current LiteDbX query engine.

This phase is intentionally later because grouped translation is the highest-risk area.

## Existing Files To Study

- `LiteDbX/Client/Database/LiteQueryable.cs`
- `LiteDbX/Client/Structures/Query.cs`
- `LiteDbX/Engine/Query/QueryOptimization.cs`
- `LiteDbX/Engine/Query/Pipeline/GroupByPipe.cs`
- `LiteDbX.Tests/Query/Aggregate_Tests.cs`
- `LiteDbX.Tests/Query/GroupBy_Tests.cs`

## Core Principle

Support only the LINQ grouping and aggregate shapes that can be mapped cleanly onto the current `Query.GroupBy`, `Query.Having`, and grouped pipeline behavior.

Do **not** try to emulate full LINQ-to-Objects `IGrouping<TKey, TElement>` behavior unless the engine model genuinely supports it.

## Work Packages

### P5.1 — Define LiteDbX LINQ `GroupBy` semantics

#### Goal
Document what `GroupBy` means for this provider.

#### Recommended scope
Support grouped aggregate projections first, for patterns like:

- `GroupBy(key).Select(g => new { g.Key, Count = g.Count() })`
- grouped projections that lower naturally to current engine capabilities

#### Not recommended for initial grouped support
- raw enumeration of `IGrouping<TKey, TElement>`
- nested composition over group sequences
- group joins or multi-source group queries

#### Acceptance criteria
A written, narrow `GroupBy` contract aligned with existing engine behavior.

---

### P5.2 — Map grouped aggregates onto existing query capabilities

#### Candidate grouped aggregates
- `Count`
- possibly `Min` / `Max`
- possibly `Sum` / `Average` if current engine support and projection lowering make them safe

#### Important note
The current native `LiteQueryable<T>.Select<K>(...)` explicitly rejects grouped typed select, which means grouped LINQ projection will likely need a dedicated translation path.

#### Acceptance criteria
A documented grouped-projection lowering strategy and a shortlist of supported aggregate shapes.

---

### P5.3 — Add `Having`-style post-group filtering where feasible

#### Goal
Support grouped filtering only if it maps cleanly onto `Query.Having`.

#### Acceptance criteria
If supported, the provider has a defined translation strategy. If not, this phase should explicitly defer it.

---

### P5.4 — Explicitly defer hard operators

#### Operators to document as deferred unless proven easy
- `Join`
- `GroupJoin`
- `SelectMany`
- `Distinct` if it requires new execution semantics
- set operators such as `Union`, `Intersect`, `Except`
- nested queryable subqueries

#### Acceptance criteria
Every deferred operator has a documented failure mode and rationale.

## Deliverables

- grouped semantics contract
- supported grouped aggregate list
- clear defer list for advanced operators
- grouped translation backlog tied to current tests

## Validation

- compare grouped behavior to current `GroupBy` and aggregate capabilities
- mine `GroupBy_Tests.cs` for skipped or partial scenarios
- ensure grouped translation still routes through the current group-by pipeline

## Suggested Test Focus

- grouped key projection
- grouped count projection
- grouped aggregate projection parity
- unsupported grouping shapes fail clearly

## Out of Scope

- full LINQ grouping parity
- multi-source query composition
- comprehensive set-operator support

## Exit Criteria

This phase is done when the team has a precise, engine-aligned definition of which grouped and advanced LINQ patterns are supported, deferred, or rejected, and that definition is backed by targeted tests or a concrete implementation backlog.

