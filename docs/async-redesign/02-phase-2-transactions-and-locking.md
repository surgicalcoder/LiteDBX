# Phase 2 — Redesign Transactions and Locking

## Objective

Replace LiteDB’s current thread-bound transaction model and blocking synchronization primitives with async-safe transaction ownership and awaitable coordination.

This is the most critical architectural phase for correctness.

## Why This Phase Exists

LiteDB currently assumes transactions are “per-thread,” with ownership and release tied to the current managed thread. That is incompatible with `await`, where continuations may resume on different threads.

Without fixing this phase, every later phase risks building async APIs on top of an invalid concurrency model.

## Prerequisites

Read:

- `ASYNC_ONLY_REDESIGN_PLAN.md`
- `docs/async-redesign/README.md`
- `docs/async-redesign/01-phase-1-contracts.md`

Understand the current transaction and locking flow in:

- `LiteDB/Engine/Services/TransactionMonitor.cs`
- `LiteDB/Engine/Services/TransactionService.cs`
- `LiteDB/Engine/Services/LockService.cs`
- `LiteDB/Engine/Engine/Transaction.cs`
- `LiteDB/Engine/Query/QueryExecutor.cs`

## Non-Negotiable Architecture Decisions

1. Transaction ownership must not depend on current thread identity.
2. Blocking synchronization primitives must not remain in async operational paths.
3. Transaction lifetime should be explicit and composable.
4. Async methods must not hold sync locks across awaits.
5. The resulting model must support query cursors and write operations safely.

## In Scope

- async transaction ownership model
- transaction monitor redesign
- transaction service redesign where required for async semantics
- lock service redesign
- collection/write/exclusive locking semantics
- query cursor interaction with transaction lifetime
- async transaction acquisition/release behavior

## Out of Scope

- full disk I/O refactor
- full query pipeline refactor
- file storage refactor
- shared mode redesign beyond documenting touchpoints
- downstream consumer updates

## Files To Inspect

### Primary files

- `LiteDB/Engine/Services/TransactionMonitor.cs`
- `LiteDB/Engine/Services/TransactionService.cs`
- `LiteDB/Engine/Services/LockService.cs`
- `LiteDB/Engine/Engine/Transaction.cs`

### Important supporting files

- `LiteDB/Engine/Query/QueryExecutor.cs`
- `LiteDB/Engine/Structures/TransactionState.cs`
- `LiteDB/Engine/Structures/TransactionPages.cs`
- `LiteDB/Engine/Structures/CursorInfo.cs`
- `LiteDB/Engine/Services/SnapShot.cs`
- `LiteDB/Engine/LiteEngine.cs`

## Current Problem Summary

The current implementation relies on:

- `ThreadLocal<TransactionService>`
- `Environment.CurrentManagedThreadId`
- `ReaderWriterLockSlim`
- `Monitor.TryEnter`
- `lock(...)`
- comments and invariants that assume a transaction belongs to a thread

This breaks down under async resumption.

## Detailed Work Items

### 1. Replace thread-bound transaction identity

Find and remove assumptions based on:

- `ThreadLocal<TransactionService>`
- `Environment.CurrentManagedThreadId`
- thread-based ownership checks

The redesign should support transaction ownership independent of thread resumption.

### 2. Implement the Phase 1 transaction contract

If Phase 1 introduced `ILiteTransaction` or equivalent, implement the engine-side ownership model around that abstraction.

Key questions to resolve:

- how transactions are created
- how nested operations reuse the current transaction
- how query-only and write transactions differ
- how explicit transactions interact with auto-transaction behavior

### 3. Redesign `TransactionMonitor`

The monitor currently:

- stores transactions in a thread slot
- uses thread identity to determine lock release behavior
- tracks cursor/query transactions in a sync world

Redesign it to:

- track transactions explicitly
- associate them with transaction scopes rather than threads
- support async lifetime and async disposal/release
- preserve correctness around cursor lifetimes and write ownership

### 4. Redesign `TransactionService`

This class should no longer encode ownership via thread id.

Pay special attention to:

- state transitions
- cursor tracking
- safepoint coordination
- commit/rollback behavior
- snapshot creation rules

It may remain mostly CPU-bound in parts, but any coordination or release paths that wait must become async-safe.

### 5. Replace `LockService`

Current responsibilities include:

- transaction gate
- collection write lock
- exclusive database lock

Replace blocking primitives with async-capable equivalents.

Potential direction:

- async transaction gate
- async collection lock registry
- async exclusive lock
- awaitable enter/exit semantics

Document fairness and starvation tradeoffs if you introduce a custom async RW lock.

### 6. Redesign transaction acquisition flow in engine commands

Review how operations currently use:

- `BeginTrans`
- `AutoTransaction`
- `CommitAndReleaseTransaction`
- cursor-open rules

Replace this with the new async transaction flow defined in Phase 1.

### 7. Handle query cursor lifetime correctly

`QueryExecutor` currently adds open cursors to a transaction and releases them on disposal of sync enumerables/readers.

This must be redesigned so that async result streaming:

- keeps the transaction alive while the result is being consumed
- releases cursor state when enumeration completes or is disposed early
- does not leak transaction lifetime on partial enumeration

### 8. Preserve transaction state correctness

Ensure correctness around:

- active/committed/aborted states
- explicit vs implicit transactions
- query-only vs write transactions
- nested usage within the same logical operation
- errors during commit/rollback/release

### 9. Add targeted design notes

Document the final transaction/locking model in a short markdown note or comments. Future phases will depend on understanding it.

## Preferred Design Direction

Use an explicit transaction scope model rather than ambient thread-bound state.

Good target properties:

- explicit ownership
- async disposal
- no thread identity dependency
- clear reuse rules for nested operations
- clear cursor lifetime rules

## Deliverables

1. Updated transaction and lock management implementation for the scoped files
2. Any new async lock or transaction abstractions needed
3. Engine transaction orchestration updated to the new model
4. A short design note describing the new transaction/locking semantics

## Acceptance Criteria

- No transaction ownership depends on current thread id
- No `ThreadLocal<TransactionService>` remains in the transaction path
- Blocking synchronization primitives are removed from async operational paths in the scoped design
- Transaction lifetime is explicit and works with async flow
- Query cursor lifetime can be safely tied to async enumeration/disposal
- Commit and rollback paths remain logically correct

## Risks and Traps

1. Using `AsyncLocal<T>` as a drop-in replacement without careful lifetime rules may hide problems.
2. Retaining partial thread-affinity assumptions will create subtle data corruption or deadlock risks later.
3. Holding an async lock across wide engine paths may cause poor throughput or deadlocks.
4. Cursor lifetime bugs can leak transactions or prematurely release them.

## Suggested Execution Order

1. Read and map all current transaction and cursor flows
2. Implement the new transaction ownership abstraction
3. Replace `TransactionMonitor` strategy
4. Replace `LockService`
5. Update engine transaction orchestration
6. Verify query cursor lifetime handling
7. Write the transaction model note

## Validation

- Verify transaction acquisition/release logic in all edited files
- Verify there are no references to thread identity for transaction ownership
- Verify no sync lock primitives remain in redesigned async paths
- Add or update focused tests if possible for nested ops, rollback, early reader disposal, and explicit transactions

## Copy/Paste Handoff Prompt

> You are working on Phase 2 of the LiteDB async-only redesign. Your task is to replace the current per-thread transaction model and blocking lock primitives with an async-safe ownership and coordination model. Do not rely on current thread id, `ThreadLocal`, `ReaderWriterLockSlim`, `Monitor`, or sync lock ownership assumptions in async operational paths. Implement the transaction/locking model defined by Phase 1, keep cursor lifetime correct, and document the resulting semantics so later phases can build on them.

