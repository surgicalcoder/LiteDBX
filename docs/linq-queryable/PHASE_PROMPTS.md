# LiteDbX LINQ / IQueryable — Copy-Paste Phase Prompts

Use these prompts with future LLM runs.

## How to use this file

1. Start with the next incomplete phase.
2. Paste the full prompt for that phase into the other LLM.
3. Attach or provide the referenced docs if the model cannot read the workspace directly.
4. Keep the model scoped to that phase unless a dependency makes a small cross-phase change unavoidable.

## Shared reminder for every phase

Every prompt below assumes this non-negotiable architecture:

- LINQ / `IQueryable<T>` must be added **on top of** the current LiteDbX query system.
- Do **not** replace `ILiteQueryable<T>`, `LiteQueryable<T>`, `Query`, or the current engine pipeline.
- Reuse the current execution path:
  - `Query`
  - `QueryOptimization`
  - `QueryPlan`
  - `QueryExecutor`
  - `QueryPipe`
  - `GroupByPipe`
- Treat `BsonMapper.GetExpression(...)` and `LinqExpressionVisitor` as lambda translators, not as the full LINQ provider.
- Keep the native `Query()` builder first-class.
- Prefer async execution semantics and avoid hidden sync-over-async behavior.

---

# Prompt 1 — Phase 1: Public Surface and Contracts

```text
You are working on Phase 1 of the LiteDbX LINQ / IQueryable roadmap.

Mission:
Define the public API shape and contract for LINQ / IQueryable<T> support in LiteDbX.
This feature must be built on top of the current query system, not as a replacement.
Do not write broad implementation code yet unless tiny supporting scaffolding is unavoidable for documenting or locking contracts.

Read these files first:
- docs/linq-queryable/MASTER_HANDOFF_PROMPT.md
- docs/linq-queryable/00-master-roadmap.md
- docs/linq-queryable/01-phase-1-public-surface-and-contracts.md
- docs/linq-queryable/README.md
- LiteDbX/Client/Database/ILiteCollection.cs
- LiteDbX/Client/Database/ILiteQueryable.cs
- LiteDbX/Client/Database/LiteQueryable.cs
- LiteDbX/Client/Database/LiteRepository.cs
- docs/ASYNC_ONLY_REDESIGN_PLAN.md
- docs/async-redesign/phase-1-decisions.md

Core requirements:
- LINQ / IQueryable<T> must be additive.
- The native Query() API must remain first-class.
- Query, QueryOptimization, QueryPlan, QueryExecutor, QueryPipe, and GroupByPipe remain the canonical execution path.
- BsonMapper.GetExpression(...) and LinqExpressionVisitor remain leaf lambda translators.
- You must explicitly decide the sync vs async execution policy.
- You must explicitly define the MVP operator matrix.

Decisions to make in this phase:
1. How users start a LINQ query, likely via AsQueryable() or equivalent.
2. Whether sync materialization is allowed, rejected, or limited.
3. Which operators are MVP, deferred, or unsupported.
4. The explicit non-goals and architecture guardrails.

Deliverables:
- a finalized public-surface decision
- a sync/async policy decision
- an operator support matrix
- architecture guardrails recorded in docs
- any necessary doc updates to keep future phases aligned

Validation expectations:
- verify the contract does not imply replacing Query()
- verify the contract respects the async-only direction of the project
- verify the contract is consistent with ILiteQueryable<T> and LiteQueryable<T>
- keep any edits narrowly scoped to design/contract docs unless a tiny API placeholder is necessary

Success checklist:
- another engineer can answer how LINQ queries start
- another engineer can answer whether sync execution is supported
- another engineer can answer which operators are in MVP
- the docs clearly state LINQ is layered on top of the existing system
```

---

# Prompt 2 — Phase 2: Queryable Provider and State Model

```text
You are working on Phase 2 of the LiteDbX LINQ / IQueryable roadmap.

Mission:
Design and, if appropriate for this phase, implement the provider shell and internal translation-state model for IQueryable<T> support.
This must be built on top of the existing LiteDbX query pipeline, not as a replacement.

Read these files first:
- docs/linq-queryable/MASTER_HANDOFF_PROMPT.md
- docs/linq-queryable/00-master-roadmap.md
- docs/linq-queryable/02-phase-2-queryable-provider-and-state-model.md
- docs/linq-queryable/01-phase-1-public-surface-and-contracts.md
- LiteDbX/Client/Database/Collections/Find.cs
- LiteDbX/Client/Database/ILiteCollection.cs
- LiteDbX/Client/Database/ILiteQueryable.cs
- LiteDbX/Client/Database/LiteQueryable.cs
- LiteDbX/Client/Mapper/BsonMapper.cs
- LiteDbX/Client/Mapper/Linq/LinqExpressionVisitor.cs
- LiteDbX/Client/Structures/Query.cs

Core requirements:
- Do not make the existing LiteQueryable<T> disappear or stop being the native builder.
- Do not bypass Query or the current engine.
- Keep method-chain translation separate from lambda-body translation.
- Avoid mutable shared query state bugs.

Focus areas:
1. Define the provider type(s) and queryable root/wrapper type(s).
2. Define the boundary between Queryable method parsing and BsonMapper/LinqExpressionVisitor lambda translation.
3. Define an internal translation state or query specification model.
4. Decide mutability, cloning, or copy-on-write rules.

Deliverables:
- provider/root design or implementation scaffold
- internal translation-state model or equivalent
- clear lowering flow from expression tree to native Query or LiteQueryable<T>
- phase notes documenting why the design preserves the current system

Validation expectations:
- trace a sample chain like Where -> OrderBy -> Select -> CountAsync through the designed flow
- verify the provider lowers into the existing Query/native query path
- verify no design decision forces LiteQueryable<T> to become the only IQueryable<T> implementation
- validate any edited files and keep changes limited to architecture scaffolding

Success checklist:
- expression-tree parsing has a clear owner
- lambda translation has a clear owner
- state/lowering has a clear owner
- query reuse/caching does not obviously risk cross-query contamination
```

---

# Prompt 3 — Phase 3: MVP Operator Translation

```text
You are working on Phase 3 of the LiteDbX LINQ / IQueryable roadmap.

Mission:
Implement the MVP translation layer for the highest-value LINQ operators on top of the existing LiteDbX query system.
Do not replace the native Query() builder. Reuse it.

Read these files first:
- docs/linq-queryable/MASTER_HANDOFF_PROMPT.md
- docs/linq-queryable/00-master-roadmap.md
- docs/linq-queryable/03-phase-3-mvp-operator-translation.md
- docs/linq-queryable/02-phase-2-queryable-provider-and-state-model.md
- LiteDbX/Client/Database/LiteQueryable.cs
- LiteDbX/Client/Database/Collections/Find.cs
- LiteDbX/Client/Mapper/BsonMapper.cs
- LiteDbX/Client/Mapper/Linq/LinqExpressionVisitor.cs
- LiteDbX/Client/Structures/Query.cs
- LiteDbX.Tests/Query/Where_Tests.cs
- LiteDbX.Tests/Query/OrderBy_Tests.cs
- LiteDbX.Tests/Query/Select_Tests.cs

MVP operators for this phase:
- Where
- OrderBy
- OrderByDescending
- ThenBy
- ThenByDescending
- Skip
- Take
- Select

Out of scope for this phase:
- GroupBy
- Join
- GroupJoin
- SelectMany
- nested subqueries
- broad advanced operator support

Implementation requirements:
- reuse BsonMapper.GetExpression(...) for supported lambda bodies
- map translated operators onto the same semantics used by Query()/LiteQueryable<T>
- add clear diagnostics for unsupported LINQ shapes
- prefer parity with native builder behavior over broader but fragile support

Deliverables:
- translation support for the MVP operators
- parity-focused tests for translated behavior
- clear unsupported-pattern exceptions/messages

Validation expectations:
- compare provider-backed behavior to collection.Query() behavior
- compare result parity with in-memory LINQ where the existing tests already use that pattern
- validate projection behavior for supported shapes only
- run focused tests for where/order/select scenarios

Success checklist:
- supported LINQ operator chains lower to the same query semantics as the native builder
- unsupported LINQ operators fail clearly
- no execution-layer replacement was introduced
```

---

# Prompt 4 — Phase 4: Async Terminals and Builder Interop

```text
You are working on Phase 4 of the LiteDbX LINQ / IQueryable roadmap.

Mission:
Make provider-backed IQueryable<T> queries executable through async terminals while preserving the native Query() builder as the canonical escape hatch.
This must run through the existing LiteDbX query pipeline.

Read these files first:
- docs/linq-queryable/MASTER_HANDOFF_PROMPT.md
- docs/linq-queryable/00-master-roadmap.md
- docs/linq-queryable/04-phase-4-async-terminals-and-builder-interop.md
- docs/linq-queryable/03-phase-3-mvp-operator-translation.md
- LiteDbX/Client/Database/ILiteQueryable.cs
- LiteDbX/Client/Database/LiteQueryable.cs
- LiteDbX/Client/Database/LiteRepository.cs
- LiteDbX/Engine/Query/QueryOptimization.cs
- LiteDbX/Engine/Query/Structures/QueryPlan.cs
- LiteDbX/Engine/Query/QueryExecutor.cs
- LiteDbX/Engine/Query/Pipeline/QueryPipe.cs

Core requirements:
- async terminals must be first-class
- do not introduce hidden sync-over-async execution
- translated LINQ queries must lower into the native Query/LiteQueryable<T>/engine path
- preserve Query() as a first-class advanced API
- preserve explain-plan/debug visibility where feasible

Suggested terminal surface:
- ToListAsync
- ToArrayAsync
- FirstAsync
- FirstOrDefaultAsync
- SingleAsync
- SingleOrDefaultAsync
- AnyAsync
- CountAsync
- LongCountAsync

Deliverables:
- async terminal execution support or the required extensions/helpers
- lowering path from provider-backed IQueryable<T> into the existing engine path
- tests proving parity with native Query() terminals
- preserved access to native escape hatches and query inspection where possible

Validation expectations:
- verify translated queries flow through QueryOptimization and QueryExecutor
- verify async terminals behave like the current LiteQueryable<T> terminals
- verify unsupported sync execution fails clearly if that is the chosen policy
- run focused execution tests

Success checklist:
- provider-backed IQueryable<T> can be executed asynchronously
- native Query() remains first-class
- provider execution does not bypass the existing query engine
```

---

# Prompt 5 — Phase 5: Grouping, Aggregates, and Advanced Translation

```text
You are working on Phase 5 of the LiteDbX LINQ / IQueryable roadmap.

Mission:
Expand the LINQ provider into grouping and advanced translation, but only where those semantics map cleanly onto the existing LiteDbX query engine.
Do not attempt to replace the native query system or emulate full LINQ-to-Objects grouping semantics if the engine does not support them.

Read these files first:
- docs/linq-queryable/MASTER_HANDOFF_PROMPT.md
- docs/linq-queryable/00-master-roadmap.md
- docs/linq-queryable/05-phase-5-grouping-aggregates-and-advanced-translation.md
- docs/linq-queryable/04-phase-4-async-terminals-and-builder-interop.md
- LiteDbX/Client/Database/LiteQueryable.cs
- LiteDbX/Client/Structures/Query.cs
- LiteDbX/Engine/Query/QueryOptimization.cs
- LiteDbX/Engine/Query/Pipeline/GroupByPipe.cs
- LiteDbX.Tests/Query/Aggregate_Tests.cs
- LiteDbX.Tests/Query/GroupBy_Tests.cs

Core requirements:
- GroupBy support must be constrained to engine-supported semantics
- favor grouped aggregate projections first
- do not promise full IGrouping<TKey, TElement> behavior unless it is genuinely supported
- preserve the native builder for advanced/manual grouped queries

Focus areas:
1. define supported LINQ GroupBy semantics
2. support grouped aggregate projection patterns that map to Query.GroupBy / Query.Having
3. document or implement supported grouped aggregates conservatively
4. explicitly reject or defer hard operators like Join, GroupJoin, SelectMany, and nested grouped composition

Deliverables:
- grouped LINQ semantics aligned to the current engine
- grouped aggregate translation support where feasible
- tests or documented backlog tied to current GroupBy/Aggregate test files
- clear failure modes for unsupported advanced operators

Validation expectations:
- verify grouped translation routes through the current group-by pipeline
- compare supported grouped behavior with native builder/group tests
- make unsupported grouping shapes fail clearly
- keep scope narrow and engine-aligned

Success checklist:
- supported grouped queries are clearly defined and tested
- unsupported grouped/advanced shapes are explicitly rejected
- no one could mistake this for full LINQ provider parity
```

---

# Prompt 6 — Phase 6: Tests, Documentation, and Rollout

```text
You are working on Phase 6 of the LiteDbX LINQ / IQueryable roadmap.

Mission:
Finish the LINQ / IQueryable<T> effort with parity-driven tests, user-facing documentation, diagnostics, and a staged rollout plan.
This LINQ layer is additive and must remain clearly positioned on top of the existing native Query() system.

Read these files first:
- docs/linq-queryable/MASTER_HANDOFF_PROMPT.md
- docs/linq-queryable/00-master-roadmap.md
- docs/linq-queryable/06-phase-6-tests-docs-and-rollout.md
- LiteDbX.Tests/Query/Where_Tests.cs
- LiteDbX.Tests/Query/OrderBy_Tests.cs
- LiteDbX.Tests/Query/Select_Tests.cs
- LiteDbX.Tests/Query/Aggregate_Tests.cs
- LiteDbX.Tests/Query/GroupBy_Tests.cs
- LiteDbX.Tests/Mapper/*
- README.md

Core requirements:
- add provider-focused tests, not just reused native-builder tests
- document how LINQ starts, how async terminals work, and when to use Query() instead
- make support boundaries explicit
- make rollout incremental and safe
- keep the native query builder clearly first-class in all docs

Deliverables:
- a parity-first test matrix
- provider-specific tests
- user-facing documentation updates
- rollout notes covering what is production-ready, experimental, or deferred
- diagnostics coverage for unsupported LINQ patterns

Validation expectations:
- run focused query and mapper tests
- ensure no regression in the native query path
- review docs for scope clarity
- verify the docs never imply LINQ replaces Query()

Success checklist:
- test coverage exists for supported LINQ behavior
- docs clearly explain LINQ vs native Query()
- support boundaries are explicit
- rollout guidance is written and realistic
```

