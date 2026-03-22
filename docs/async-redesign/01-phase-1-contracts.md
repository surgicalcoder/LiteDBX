# Phase 1 — Define the Async-Only Contracts

## Objective

Redesign LiteDB’s public and engine-facing contracts so async is the only supported execution model.

This phase should produce the **target interface layer and lifecycle abstractions** that all later phases will implement.

## Why This Phase Exists

The rest of the redesign cannot proceed safely until the destination contract is clear. Right now the codebase is sync-first throughout the client, engine, query, and storage layers. Later phases need stable interface targets so they do not redesign internals against moving assumptions.

## Prerequisites

Read:

- `ASYNC_ONLY_REDESIGN_PLAN.md`
- `docs/async-redesign/README.md`

Understand the current synchronous APIs in:

- `LiteDB/Client/Database/ILiteDatabase.cs`
- `LiteDB/Client/Database/ILiteCollection.cs`
- `LiteDB/Client/Database/ILiteQueryable.cs`
- `LiteDB/Client/Database/ILiteRepository.cs`
- `LiteDB/Client/Storage/ILiteStorage.cs`
- `LiteDB/Engine/ILiteEngine.cs`
- `LiteDB/Document/DataReader/IBsonDataReader.cs`

## Non-Negotiable Architecture Decisions

1. Public methods must not use the `Async` suffix.
2. Synchronous public alternatives should not be preserved.
3. Query execution should target `IAsyncEnumerable<T>` or a truly async cursor abstraction.
4. Transactions should move away from per-thread implicit ownership.
5. Long-lived resources should use `IAsyncDisposable` where appropriate.
6. Do not introduce fake async wrappers around sync code.

## In Scope

### Interfaces and contracts

- `ILiteDatabase`
- `ILiteCollection<T>`
- `ILiteQueryable<T>`
- `ILiteQueryableResult<T>`
- `ILiteRepository`
- `ILiteStorage<TFileId>`
- `ILiteEngine`
- `IBsonDataReader` or its replacement abstraction
- a new async transaction abstraction if needed
- async file handle abstractions if needed

### Lifecycle contract changes

- database open/create pattern
- engine creation/open pattern
- async disposal surface

### Naming and return type rules

- plain method names
- `Task`, `ValueTask`, and `IAsyncEnumerable<T>` only where operationally appropriate

## Out of Scope

- Full implementation of async transactions
- Locking internals
- Disk I/O internals
- Query engine refactor
- File storage implementation refactor
- `SharedEngine` redesign details
- Full downstream consumer updates

This phase is about **defining** the target surface, not implementing the entire stack.

## Files To Inspect

### Primary files

- `LiteDB/Client/Database/ILiteDatabase.cs`
- `LiteDB/Client/Database/ILiteCollection.cs`
- `LiteDB/Client/Database/ILiteQueryable.cs`
- `LiteDB/Client/Database/ILiteRepository.cs`
- `LiteDB/Client/Storage/ILiteStorage.cs`
- `LiteDB/Engine/ILiteEngine.cs`
- `LiteDB/Document/DataReader/IBsonDataReader.cs`

### Important implementation references

- `LiteDB/Client/Database/LiteDatabase.cs`
- `LiteDB/Client/Database/LiteCollection.cs`
- `LiteDB/Client/Database/LiteQueryable.cs`
- `LiteDB/Client/Database/LiteRepository.cs`
- `LiteDB/Client/Storage/LiteStorage.cs`
- `LiteDB/Client/Storage/LiteFileStream.cs`
- `LiteDB/Engine/LiteEngine.cs`
- `LiteDB/Client/Shared/SharedEngine.cs`
- `LiteDB/Document/DataReader/BsonDataReader.cs`

## Detailed Work Items

### 1. Redesign `ILiteDatabase`

Decide and define:

- async-only open/create pattern
- async transaction start/ownership
- async SQL execution contract
- async checkpoint/rebuild contract
- async disposal contract

Expected changes likely include:

- remove sync `BeginTrans`, `Commit`, `Rollback`
- remove sync `Execute`
- replace `Dispose` expectations with `IAsyncDisposable`
- define whether `Execute` returns `IAsyncEnumerable<BsonDocument>`, an async reader abstraction, or both

### 2. Redesign `ILiteCollection<T>`

Convert all collection operations to async-only forms.

Pay attention to:

- single-entity CRUD
- batch CRUD
- query-returning methods
- index operations
- count/exists/aggregate methods
- methods returning enumerables today

Decide whether:

- `Find(...)` returns `IAsyncEnumerable<T>`
- `FindById(...)` returns `ValueTask<T>`
- aggregate operations use `ValueTask<T>` or `Task<T>`

### 3. Redesign `ILiteQueryable<T>` and `ILiteQueryableResult<T>`

Keep query composition cheap and synchronous, but make execution async-only.

Define target behavior for:

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

### 4. Redesign `ILiteRepository`

Convert repository-level convenience methods to async-only signatures.

The repository is just a wrapper, but it still needs to align with the new contract and async disposal model.

### 5. Redesign `ILiteStorage<TFileId>`

Define async-only file storage APIs.

Important: decide whether public storage can still expose `Stream`-derived types. If the project requirement is truly zero sync API surface, public `Stream` usage must be replaced or heavily constrained.

### 6. Redesign `ILiteEngine`

This should become the async-only engine contract that later phases implement.

It must align with:

- async transactions
- async query execution
- async writes and maintenance operations
- async disposal

### 7. Decide the future of `IBsonDataReader`

Choose one:

#### Option A — Replace it with async streams
Prefer `IAsyncEnumerable<BsonDocument>` / `IAsyncEnumerable<BsonValue>` where possible.

#### Option B — Keep it as an async cursor
If keeping it, redesign it to something like:

- `Read() -> ValueTask<bool>`
- `Current`
- `DisposeAsync() -> ValueTask`

If you retain it, document why.

### 8. Define new supporting abstractions if needed

Likely candidates:

- `ILiteTransaction`
- async file read/write session interfaces
- async SQL result abstraction
- async maintenance/rebuild abstractions if needed

### 9. Document the contract decisions

At the end of this phase, there should be a short design note or updated markdown explaining key contract choices, especially where there were multiple viable options.

## Preferred Contract Direction

Use these as defaults unless implementation realities force a different but clearly documented direction:

- `ValueTask<T>` for simple single-result operations that often complete quickly
- `Task<T>` only where `ValueTask<T>` does not fit well
- `IAsyncEnumerable<T>` for streamed result sets
- `IAsyncDisposable` for long-lived async resources
- explicit transaction objects instead of ambient per-thread state

## Deliverables

1. Updated contract/interface files
2. Any new abstraction interfaces needed for async transactions or async file handles
3. Lifecycle contract changes for async open/dispose
4. A short design note summarizing contract decisions and unresolved questions

## Acceptance Criteria

- No synchronous public APIs remain in the redesigned contract set for the scoped files
- No `Async` suffixes are introduced on public methods
- Query execution contract is clearly async-only
- Transaction ownership contract is explicit and async-safe
- Resource lifecycle is async-aware where needed
- The new contracts are coherent enough for Phases 2–6 to implement against

## Risks and Traps

1. Keeping too much of the old sync shape will make later phases harder.
2. Preserving `IBsonDataReader` without a strong reason may complicate the query redesign.
3. Deferring transaction contract decisions will block Phase 2.
4. Leaving public `Stream` exposure unresolved will block Phase 5.

## Suggested Execution Order

1. Inspect all existing interfaces and wrappers
2. Decide transaction abstraction
3. Decide query result abstraction
4. Decide public file abstraction direction
5. Update interfaces
6. Add supporting abstractions
7. Write a short decisions note

## Validation

- Ensure the contract files compile together after the redesign
- Ensure there are no leftover sync signatures in the scoped interface layer
- Verify all public method names remain suffix-free
- Verify lifecycle changes are reflected consistently across database/engine/repository/storage contracts

## Copy/Paste Handoff Prompt

> You are working on Phase 1 of the LiteDB async-only redesign. Your task is to redefine the public and engine-facing contracts so async is the default and only execution model. Do not preserve synchronous public APIs. Do not add `Async` suffixes. Prefer `ValueTask<T>`, `Task<T>`, `IAsyncEnumerable<T>`, and `IAsyncDisposable` where appropriate. You are not implementing the whole engine yet; you are defining the target contracts and any supporting abstractions needed by later phases. Review `ASYNC_ONLY_REDESIGN_PLAN.md` and `docs/async-redesign/README.md`, then update the scoped interfaces and add a short design note summarizing key contract choices.

