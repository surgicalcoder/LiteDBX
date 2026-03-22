# Phase 5 — Redesign File Storage as Async-Only

## Objective

Refactor LiteDB’s file storage subsystem so it becomes fully async and aligned with the new async-only public API.

This phase must resolve whether public file APIs can continue to expose `Stream` or whether they need a dedicated async-only abstraction.

## Why This Phase Exists

The current file subsystem is built around `LiteFileStream<TFileId> : Stream` and uses synchronous read/write/copy behavior. If the project requirement is “no sync in there,” then the current file abstraction is fundamentally incompatible with the redesign.

## Prerequisites

Read:

- `ASYNC_ONLY_REDESIGN_PLAN.md`
- `docs/async-redesign/README.md`
- `docs/async-redesign/01-phase-1-contracts.md`
- `docs/async-redesign/03-phase-3-disk-and-streams.md`
- `docs/async-redesign/04-phase-4-query-pipeline.md`

Inspect current file storage code:

- `LiteDB/Client/Storage/ILiteStorage.cs`
- `LiteDB/Client/Storage/LiteStorage.cs`
- `LiteDB/Client/Storage/LiteFileStream.cs`
- `LiteDB/Client/Storage/LiteFileStream.Read.cs`
- `LiteDB/Client/Storage/LiteFileStream.Write.cs`
- `LiteDB/Client/Storage/LiteFileInfo.cs`

## Non-Negotiable Architecture Decisions

1. Public storage APIs must be async-only.
2. Do not preserve synchronous upload/download/open methods.
3. If zero sync public surface is required, public `Stream` inheritance must be removed.
4. Do not reintroduce sync file-copy behavior (`CopyTo`, sync `File.OpenRead`, etc.) in the redesigned paths.
5. Storage metadata and chunk operations must align with async transaction and storage infrastructure.

## In Scope

- `ILiteStorage<TFileId>` redesign implementation
- `LiteStorage<TFileId>` refactor
- `LiteFileStream<TFileId>` redesign or replacement
- upload/download/open behavior
- file/chunk read and write flow
- metadata update flow if it interacts with storage operations
- any new public async file handle abstractions

## Out of Scope

- `SharedEngine` redesign
- full downstream consumer migration
- non-storage query pipeline internals beyond what storage calls rely on

## Files To Inspect

### Primary files

- `LiteDB/Client/Storage/ILiteStorage.cs`
- `LiteDB/Client/Storage/LiteStorage.cs`
- `LiteDB/Client/Storage/LiteFileStream.cs`
- `LiteDB/Client/Storage/LiteFileStream.Read.cs`
- `LiteDB/Client/Storage/LiteFileStream.Write.cs`
- `LiteDB/Client/Storage/LiteFileInfo.cs`

### Supporting files

- `LiteDB/Client/Database/ILiteDatabase.cs`
- `LiteDB/Client/Database/LiteDatabase.cs`
- `LiteDB/Client/Database/ILiteCollection.cs`
- `LiteDB/Client/Database/LiteCollection.cs`
- `LiteDB/Utils/MimeTypeConverter.cs`

## Current Problem Summary

The current storage system relies on:

- `Stream` inheritance
- synchronous `Read`, `Write`, `Flush`, `Seek`
- synchronous `CopyTo`
- synchronous file open/copy helpers
- synchronous chunk reads and writes through collection operations

That is incompatible with an async-only surface.

## Detailed Work Items

### 1. Finalize the public file abstraction

Decide whether to:

- replace `LiteFileStream<TFileId>` entirely with an async-only file session abstraction
- keep a type named `LiteFileStream<TFileId>` but remove `Stream` inheritance and redefine its contract

If the requirement is strict, public `Stream` inheritance must go.

### 2. Redesign `ILiteStorage<TFileId>`

Convert operations such as:

- `FindById`
- `Find`
- `FindAll`
- `Exists`
- `OpenRead`
- `OpenWrite`
- `Upload`
- `Download`
- `SetMetadata`
- `Delete`

Use async result types consistently and align query-returning methods with the new query model.

### 3. Refactor `LiteStorage<TFileId>`

Reimplement storage methods using the async collection/database APIs from earlier phases.

Make sure:

- file metadata lookup is async
- chunk traversal is async
- upload/download flows are async end to end
- metadata updates align with async writes

### 4. Replace sync chunk reading in file reads

The current read path fetches chunks synchronously through collection queries.

Refactor this to use the async query and collection infrastructure.

### 5. Replace sync chunk writing in file writes

The current write path buffers and inserts chunks synchronously.

Refactor to async chunk persistence and async file metadata finalization.

### 6. Remove sync copy helpers

Audit and replace behavior that depends on:

- `CopyTo`
- sync `File.OpenRead`
- sync file save/load helpers
- sync `Flush`

### 7. Decide how seeking behaves

If the new public file handle is async-only, decide whether:

- seeking remains synchronous as a pure state change
- or the abstraction changes to chunk-oriented sequential async access only

Document the decision.

### 8. Handle disposal and finalization correctly

If the writer must flush or finalize metadata when closed, use `IAsyncDisposable` and async finalization semantics.

Avoid sync finalizers that conceal unfinished async work.

### 9. Review file info semantics

`LiteFileInfo<TFileId>` may need updates depending on how read/write sessions and metadata flows change.

### 10. Document the public storage model

Write down:

- whether `Stream` remains anywhere public
- how reads/writes are modeled
- how finalization occurs
- how metadata updates interact with file content writes

## Preferred Design Direction

- Public storage is async-only
- Public `Stream` inheritance is removed if zero sync surface is required
- Async read/write sessions are explicit
- Upload/download use async streams or async file handles consistently
- Finalization/disposal is explicit and async

## Deliverables

1. Updated storage contracts and implementation for scoped files
2. New async public file handle abstraction if needed
3. Async read/write/upload/download flows
4. Storage-model design note documenting key decisions

## Acceptance Criteria

- No synchronous public storage methods remain in scope
- Public file storage no longer relies on sync `Stream` semantics if zero sync surface is required
- File read/write flows are async end to end
- Metadata finalization is async-safe
- Chunk traversal and persistence align with earlier async collection/query/storage layers

## Risks and Traps

1. Keeping `Stream` publicly may preserve sync API surface even if implementation is async.
2. Writer finalization bugs can corrupt metadata or leave partial chunk state.
3. Seeking semantics may become confusing if not documented clearly.
4. Hidden sync file helper calls can invalidate the whole phase goal.

## Suggested Execution Order

1. Decide public file abstraction shape
2. Redesign `ILiteStorage<TFileId>`
3. Replace/refactor `LiteFileStream<TFileId>`
4. Refactor `LiteStorage<TFileId>` read/write flows
5. Remove sync copy/open helpers
6. Document storage-model decisions

## Validation

- Verify no synchronous public storage API remains
- Verify no public `Stream` inheritance remains if strict zero-sync surface is required
- Validate upload/download/read/write and metadata finalization paths
- Run storage tests if available, especially around partial writes, empty files, and disposal

## Copy/Paste Handoff Prompt

> You are working on Phase 5 of the LiteDB async-only redesign. Your task is to redesign the file storage subsystem so it becomes async-only and compatible with the earlier contract, transaction, query, and storage-layer changes. Do not preserve synchronous public file APIs. If zero sync surface is required, remove public `Stream` inheritance and introduce an async-only file/session abstraction. Ensure upload, download, chunk reads/writes, metadata finalization, and disposal are async-safe end to end.

