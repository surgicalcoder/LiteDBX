# Phase 3 — Make Disk and Stream Infrastructure Async-First

## Objective

Refactor LiteDB’s storage layer so file access, WAL persistence, page reads/writes, stream pooling, stream wrappers, and lifecycle operations are genuinely asynchronous.

This phase provides the async I/O substrate required by queries, transactions, rebuilds, and file storage.

## Why This Phase Exists

Even if the public API becomes async-only, the current storage layer is still mostly synchronous. Some isolated async methods exist, but the underlying path still relies on sync I/O or wrappers that do not implement async correctly.

Without this phase, later async code would still block threads or degrade into fake async.

## Prerequisites

Read:

- `ASYNC_ONLY_REDESIGN_PLAN.md`
- `docs/async-redesign/README.md`
- `docs/async-redesign/01-phase-1-contracts.md`
- `docs/async-redesign/02-phase-2-transactions-and-locking.md`

Understand the current disk and stream stack in:

- `LiteDB/Engine/Disk/DiskService.cs`
- `LiteDB/Engine/Disk/StreamFactory/IStreamFactory.cs`
- `LiteDB/Engine/Disk/StreamFactory/FileStreamFactory.cs`
- `LiteDB/Engine/Disk/StreamFactory/StreamPool.cs`
- `LiteDB/Engine/Disk/Streams/AesStream.cs`
- `LiteDB/Engine/Disk/Streams/ConcurrentStream.cs`
- `LiteDB/Engine/Disk/Streams/TempStream.cs`

## Non-Negotiable Architecture Decisions

1. No fake async wrappers.
2. File and stream operations in operational paths must be actually asynchronous.
3. Wrapper streams must implement async behavior properly if they remain part of the design.
4. Async lifecycle and disposal must be supported where close paths flush or release I/O resources.
5. Do not reintroduce sync waiting or lock ownership assumptions around I/O.

## In Scope

- `DiskService`
- stream factory abstractions
- file stream factory behavior
- stream pools
- stream wrappers
- WAL/datafile async persistence
- async initialization/open/close support for storage infrastructure
- helper methods related to flushing or file manipulation in operational paths

## Out of Scope

- full query pipeline redesign
- file storage public API redesign
- shared mode redesign except where storage interfaces affect it
- downstream consumer updates

## Files To Inspect

### Primary files

- `LiteDB/Engine/Disk/DiskService.cs`
- `LiteDB/Engine/Disk/StreamFactory/IStreamFactory.cs`
- `LiteDB/Engine/Disk/StreamFactory/FileStreamFactory.cs`
- `LiteDB/Engine/Disk/StreamFactory/StreamPool.cs`
- `LiteDB/Engine/Disk/Streams/AesStream.cs`
- `LiteDB/Engine/Disk/Streams/ConcurrentStream.cs`
- `LiteDB/Engine/Disk/Streams/TempStream.cs`

### Supporting files

- `LiteDB/Utils/Extensions/StreamExtensions.cs`
- `LiteDB/Engine/Disk/DiskReader.cs`
- `LiteDB/Engine/Disk/MemoryCache.cs`
- `LiteDB/Engine/Sort/SortDisk.cs`
- `LiteDB/Engine/LiteEngine.cs`
- `LiteDB/Engine/Services/WalIndexService.cs`
- `LiteDB/Engine/Services/TransactionService.cs`

## Current Problem Summary

The storage stack still contains:

- synchronous page reads/writes
- synchronous WAL persistence
- synchronous file length changes
- synchronous flush paths
- wrapper streams without proper async overrides
- lifecycle behavior that assumes sync open/close

## Detailed Work Items

### 1. Redesign `IStreamFactory`

Ensure the stream factory abstraction supports async-first usage.

Questions to resolve:

- Does stream acquisition itself need to be async?
- How should open/create semantics expose async initialization?
- What lifecycle/disposal behavior is expected of factory-created resources?

### 2. Refactor `FileStreamFactory`

Review how file streams are opened and configured.

Goals:

- make async usage the default assumption
- ensure buffer sizes/options align with async access patterns
- preserve encryption/wrapper compatibility
- preserve correctness around readonly/write sharing modes

### 3. Refactor `StreamPool`

The pool currently assumes synchronous resource management.

Update it to support:

- async-safe acquisition/return if needed
- async disposal for pooled resources
- correct lifecycle for writer streams
- no sync-only close behavior if flush or disposal can await

### 4. Refactor `DiskService`

This is the core of the phase.

Convert responsibilities such as:

- initialization
- reading pages
- reading full files when required
- writing WAL pages
- writing direct data pages
- resizing files
- invalid-state marking
- shutdown/flush paths

Important: do not merely add async entry points if internals still call sync I/O.

### 5. Refactor wrapper streams

#### `AesStream`
Ensure encryption/decryption read/write paths support proper async flow.

#### `ConcurrentStream`
Ensure concurrent access behavior supports async read/write/flush semantics.

#### `TempStream`
Ensure temp-file/memory overflow behavior is async-safe.

If these wrappers remain `Stream` implementations, they must override and implement async methods properly.

### 6. Review sorting temp-disk behavior

`SortDisk` and related code already contain partial async behavior.

Make sure it is consistent with the redesigned stream/disk infrastructure and does not rely on sync writer locking or partial async behavior.

### 7. Review flush semantics

Existing sync helpers like `FlushToDisk()` may not map cleanly into the async design.

Decide how to represent:

- async flush
- durable flush
- checkpoint-related durability requirements

Document any platform constraints.

### 8. Support async lifecycle hooks for engine open/close

This phase should provide the primitives needed by the later async `LiteEngine` open/close lifecycle.

You may not finish the full engine lifecycle here, but storage infrastructure should be ready for it.

### 9. Document I/O decisions

Write down important storage-layer decisions, especially if:

- some operations remain CPU-bound synchronously by necessity
- some `Stream` abstractions stay internal only
- some OS-level durability operations cannot be made perfectly async

## Preferred Design Direction

- Use true stream async APIs throughout read/write paths
- Avoid sync file APIs in runtime paths
- Make pooled resource disposal async-aware
- Keep storage abstractions internal and composable
- If a wrapper cannot support correct async semantics, redesign or replace it

## Deliverables

1. Async-first storage layer implementation for scoped files
2. Updated stream factory and pool abstractions
3. Correct async stream wrapper implementations or replacements
4. Storage-layer design notes for durability, flush, and wrapper behavior

## Acceptance Criteria

- No synchronous file I/O remains in the main operational storage paths for the scoped design
- WAL/datafile persistence has genuine async implementation paths
- Wrapper streams do not silently fall back to sync behavior for async operations
- Resource disposal/open behavior is compatible with the async-only lifecycle
- Later phases can safely depend on the storage layer without reintroducing blocking I/O

## Risks and Traps

1. Leaving one sync wrapper in the middle of the path can silently degrade the whole pipeline.
2. Overusing locks around async stream writes can reduce throughput badly.
3. Async file length/flush semantics must preserve correctness, not just API shape.
4. Some OS-level durability operations may require careful documentation if fully async behavior is limited.

## Suggested Execution Order

1. Inspect the full storage stack end to end
2. Redesign stream factory/pool contracts as needed
3. Refactor wrapper streams
4. Refactor `DiskService` reads/writes/flush/resize
5. Reconcile `SortDisk` and related temp-disk paths
6. Add storage design notes

## Validation

- Verify edited files no longer use sync read/write APIs in main runtime paths
- Verify wrapper streams override async methods where needed
- Verify disposal logic is consistent with async lifecycle expectations
- Run focused storage tests if available

## Copy/Paste Handoff Prompt

> You are working on Phase 3 of the LiteDB async-only redesign. Your task is to make the disk and stream infrastructure genuinely asynchronous. Do not add async-looking methods that still rely on sync I/O internally. Refactor `DiskService`, stream factories, stream pools, and wrapper streams so WAL writes, page reads/writes, flushes, and lifecycle operations are truly async-safe and compatible with the async-only architecture defined in earlier phases.

