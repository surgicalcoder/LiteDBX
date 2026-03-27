# Phase 6 — Tests, Documentation, and Rollout

## Phase Goal

Finish the LINQ / `IQueryable<T>` effort with parity-driven tests, user-facing docs, diagnostics, and a staged rollout plan.

## Existing Files To Study

- `LiteDbX.Tests/Query/Where_Tests.cs`
- `LiteDbX.Tests/Query/OrderBy_Tests.cs`
- `LiteDbX.Tests/Query/Select_Tests.cs`
- `LiteDbX.Tests/Query/Aggregate_Tests.cs`
- `LiteDbX.Tests/Query/GroupBy_Tests.cs`
- `LiteDbX.Tests/Mapper/*`
- `README.md`

## Work Packages

### P6.1 — Build a parity-first test matrix

#### Goal
Tie every supported LINQ operator to existing native query tests or new provider-specific tests.

#### Recommended matrix categories
- translation-only tests
- result parity with `collection.Query()`
- parity with in-memory LINQ where feasible
- query plan/debug visibility tests
- unsupported-pattern diagnostics tests

#### Acceptance criteria
A written matrix that maps each supported operator to test coverage.

---

### P6.2 — Add provider-focused tests

#### Goal
Ensure the new LINQ layer is tested as a layer, not just through reused native builder tests.

#### Recommended coverage
- provider query composition
- async terminal execution
- projection materialization
- unsupported LINQ patterns
- grouped support if implemented

#### Acceptance criteria
New tests prove provider behavior rather than only native builder behavior.

---

### P6.3 — Write user-facing documentation

#### Required docs
- how to start a LINQ query
- when to use LINQ vs native `Query()`
- supported operators
- unsupported operators
- async terminal usage
- escape hatches for advanced scenarios

#### Acceptance criteria
The main repo docs make it impossible to mistake this for a complete replacement of the native query builder.

---

### P6.4 — Define rollout and compatibility strategy

#### Goal
Introduce the LINQ layer safely.

#### Recommended rollout notes
- launch as additive API surface
- keep native query builder examples in docs
- explicitly label support level for LINQ operators
- document likely future expansion areas separately from current guarantees

#### Acceptance criteria
There is a written statement of what is production-ready, experimental, or deferred.

## Deliverables

- test matrix
- new provider-specific tests
- documentation updates
- rollout notes and support boundaries

## Validation

- run focused query and mapper tests
- ensure no regression in current query behavior
- review docs for scope clarity and escape-hatch guidance

## Suggested Test Focus

- `Where` parity
- ordering parity
- projection parity
- async terminal parity
- unsupported operator diagnostics
- grouped coverage if applicable

## Out of Scope

- redesigning existing native query docs from scratch
- broadening supported LINQ scope beyond implemented features

## Exit Criteria

This phase is done when the LINQ layer has:

1. targeted automated coverage
2. clear user-facing documentation
3. explicit support boundaries
4. a rollout story that keeps the native query system clearly first-class

