# Master Handoff Prompt — LiteDbX LINQ / IQueryable Work

You are implementing one phase of the LiteDbX LINQ / `IQueryable<T>` roadmap.

## Mission

Add LINQ / `IQueryable<T>` support **on top of** the current LiteDbX query system.

Do **not** replace the existing query builder or execution pipeline.

## Non-Negotiable Rules

1. Keep `ILiteQueryable<T>` / `LiteQueryable<T>` as a first-class native query API.
2. Keep `Query`, `QueryOptimization`, `QueryPlan`, `QueryExecutor`, `QueryPipe`, and `GroupByPipe` as the canonical execution path.
3. Treat `BsonMapper.GetExpression(...)` and `LinqExpressionVisitor` as lambda/body translators, not as the entire LINQ provider.
4. Respect the async-only direction of the codebase. Avoid introducing hidden sync-over-async execution paths.
5. Do not broaden scope beyond the assigned phase unless a dependency absolutely requires it.
6. Preserve existing public behavior unless the phase doc explicitly says otherwise.
7. Unsupported LINQ constructs must fail with clear diagnostics.
8. Validate all edited files and run focused tests.
9. Do not run any builds or tests yourself, that will be done manually.

## Existing Architecture To Reuse

- `LiteDbX/Client/Database/ILiteCollection.cs`
- `LiteDbX/Client/Database/ILiteQueryable.cs`
- `LiteDbX/Client/Database/LiteQueryable.cs`
- `LiteDbX/Client/Database/LiteRepository.cs`
- `LiteDbX/Client/Mapper/BsonMapper.cs`
- `LiteDbX/Client/Mapper/Linq/LinqExpressionVisitor.cs`
- `LiteDbX/Client/Structures/Query.cs`
- `LiteDbX/Engine/Query/QueryOptimization.cs`
- `LiteDbX/Engine/Query/Structures/QueryPlan.cs`
- `LiteDbX/Engine/Query/QueryExecutor.cs`
- `LiteDbX/Engine/Query/Pipeline/QueryPipe.cs`
- `LiteDbX/Engine/Query/Pipeline/GroupByPipe.cs`

## Expected Working Style

1. Read the assigned phase doc first.
2. Trace every referenced symbol before changing code.
3. Reuse the current query engine rather than bypassing it.
4. Prefer incremental, testable changes.
5. Add or extend tests that prove parity with the existing fluent query builder.
6. Leave clear notes if the phase uncovers a blocking issue or scope mismatch.

## Deliverables Per Phase

- implementation limited to the assigned phase
- tests for newly supported behavior
- clear unsupported behavior for out-of-scope LINQ shapes
- brief notes summarizing what changed, what remains, and any new decisions

## Suggested Validation Checklist

- query translation correctness
- execution through existing `Query` pipeline
- async terminal behavior
- parity with existing `Query()` fluent API
- plan/debug visibility where applicable
- no accidental regression in existing query tests

