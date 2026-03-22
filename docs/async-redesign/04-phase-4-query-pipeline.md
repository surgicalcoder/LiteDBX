# Phase 4 — Convert the Query Pipeline to Async Streaming

## Objective

Refactor LiteDB’s query execution and result consumption model from synchronous enumeration/cursors to async streaming.

This phase should make query execution compatible with the async-only public API and the async-safe transaction/storage foundations from earlier phases.

## Why This Phase Exists

The current query pipeline is built around `IEnumerable<T>`, `IEnumerator<T>`, and synchronous reader methods. Even if the engine and storage layers become async-capable, queries will still be sync-bound until their execution model changes.

## Prerequisites

Read:

- `ASYNC_ONLY_REDESIGN_PLAN.md`
- `docs/async-redesign/README.md`
- `docs/async-redesign/01-phase-1-contracts.md`
- `docs/async-redesign/02-phase-2-transactions-and-locking.md`
- `docs/async-redesign/03-phase-3-disk-and-streams.md`

Inspect the current query flow in:

- `LiteDB/Client/Database/LiteQueryable.cs`
- `LiteDB/Document/DataReader/IBsonDataReader.cs`
- `LiteDB/Document/DataReader/BsonDataReader.cs`
- `LiteDB/Document/DataReader/BsonDataReaderExtensions.cs`
- `LiteDB/Client/Shared/SharedDataReader.cs`
- `LiteDB/Engine/Query/QueryExecutor.cs`

## Non-Negotiable Architecture Decisions

1. Query execution must be async-only.
2. Query composition may remain synchronous if it is pure in-memory builder logic.
3. Result consumption should prefer `IAsyncEnumerable<T>` unless a well-justified async cursor abstraction is retained.
4. Cursor/reader disposal must be async-safe.
5. Query lifetime must interact correctly with async transaction lifetime.

## In Scope

- query execution contracts and implementations
- query terminal operations
- data reader/cursor abstraction or its replacement
- async result materialization
- query cursor lifetime
- query pipeline integration with async transaction and storage layers

## Out of Scope

- file storage redesign
- shared mode redesign except reader touchpoints
- downstream shell/consumer updates beyond code needed for compile correctness

## Files To Inspect

### Primary files

- `LiteDB/Client/Database/ILiteQueryable.cs`
- `LiteDB/Client/Database/LiteQueryable.cs`
- `LiteDB/Client/Database/Collections/Find.cs`
- `LiteDB/Document/DataReader/IBsonDataReader.cs`
- `LiteDB/Document/DataReader/BsonDataReader.cs`
- `LiteDB/Document/DataReader/BsonDataReaderExtensions.cs`
- `LiteDB/Client/Shared/SharedDataReader.cs`
- `LiteDB/Engine/Query/QueryExecutor.cs`

### Supporting files

- `LiteDB/Engine/Query/**`
- `LiteDB/Engine/SystemCollections/**`
- `LiteDB/Engine/Engine/Query.cs`
- `LiteDB/Client/Database/Collections/Aggregate.cs`
- `LiteDB/Client/Database/LiteDatabase.cs`
- `LiteDB/Engine/Services/TransactionMonitor.cs`

## Current Problem Summary

The current query pipeline depends on:

- sync enumerables
- sync reader iteration
- sync materializers (`ToList`, `First`, `Single`, etc.)
- sync reader disposal behavior
- sync transaction/cursor coordination

That model must be replaced or deeply refactored.

## Detailed Work Items

### 1. Finalize the query result abstraction

Based on Phase 1, decide whether the primary model is:

- `IAsyncEnumerable<T>`
- an async cursor/reader abstraction
- or a combination where internal readers back public async streams

Use the simpler model unless a reader abstraction is truly necessary.

### 2. Refactor `LiteQueryable<T>`

Keep builder methods synchronous if they only mutate/query the in-memory query definition.

Convert terminal operations to async-only:

- `ToEnumerable`
- `ToDocuments`
- `ToList`
- `ToArray`
- `First`
- `FirstOrDefault`
- `Single`
- `SingleOrDefault`
- `Count`
- `LongCount`
- `Exists`
- `Into`
- `ExecuteReader` if retained

### 3. Refactor collection find/query entry points

Methods in collection partials like `Find`, `FindAll`, `FindOne`, and other query shortcuts should align with the new async result model.

### 4. Refactor `QueryExecutor`

This is the engine center of the phase.

Make query execution compatible with:

- async transaction lifetime
- async storage reads
- async pipeline iteration
- async disposal/cursor release

### 5. Replace or redesign `BsonDataReader`

If the project keeps a reader abstraction, redesign it for async iteration and async disposal.

If the project moves fully to async streams, replace it where practical and keep any internal cursor type minimal.

### 6. Update `BsonDataReaderExtensions`

These helper extensions are sync-oriented today.

They must either:

- be replaced with async equivalents
- or removed if the result model becomes direct `IAsyncEnumerable<T>` consumption

### 7. Update shared reader wrappers if still needed

`SharedDataReader` and related wrappers must align with the new reader/streaming model and async disposal semantics.

### 8. Review pipeline components under `LiteDB/Engine/Query`

Determine which parts can remain synchronous because they are purely CPU-bound transformations and which parts must become async because they depend on storage or cursor advancement.

Avoid unnecessary taskification of pure CPU transforms.

### 9. Verify system collection queries

System collections may have different data sources from user collections. Ensure the query execution abstraction still works consistently for them.

### 10. Document query execution decisions

Capture:

- chosen result abstraction
- disposal model
- cursor lifetime behavior
- where synchronous internal transforms remain acceptable

## Preferred Design Direction

- Public query execution returns `IAsyncEnumerable<T>` where possible
- Terminal materializers return `ValueTask<T>` / `ValueTask<List<T>>`
- Internal async cursor abstractions stay minimal and hidden if possible
- Builder methods remain synchronous and cheap

## Deliverables

1. Async query execution surface and implementation for scoped files
2. Reworked `LiteQueryable<T>` terminal methods
3. Reworked data reader/cursor abstraction or replacement
4. Short note documenting query model decisions and lifecycle semantics

## Acceptance Criteria

- Query execution is async-only in the scoped surface
- Query terminal operations no longer depend on sync enumeration
- Result streaming works with async transaction lifetime
- Reader/cursor disposal is async-safe
- Builder methods remain cheap and do not perform unnecessary async work

## Risks and Traps

1. Prematurely taskifying pure CPU pipeline pieces will add overhead without benefit.
2. Cursor disposal bugs can leak transactions or hold resources too long.
3. Materializer methods must avoid double enumeration or hidden buffering bugs.
4. System collection query behavior may diverge if not validated explicitly.

## Suggested Execution Order

1. Decide the final result abstraction
2. Refactor `LiteQueryable<T>` terminal methods
3. Refactor `QueryExecutor`
4. Replace/redesign data reader types
5. Update collection query entry points
6. Validate system collection behavior
7. Write query-model notes

## Validation

- Verify all scoped query APIs are async-only
- Verify no sync enumerator-based reader path remains in the main query execution flow
- Validate early termination and disposal behavior
- Run query tests if available, especially around first/single/count/materialization

## Copy/Paste Handoff Prompt

> You are working on Phase 4 of the LiteDB async-only redesign. Your task is to convert the query pipeline from synchronous enumeration/cursor behavior to async streaming. Prefer `IAsyncEnumerable<T>` unless a minimal async cursor abstraction is truly necessary. Keep query builder methods synchronous if they are pure in-memory composition. Ensure result streaming, terminal materializers, and disposal all work correctly with the async transaction and storage layers from earlier phases.

