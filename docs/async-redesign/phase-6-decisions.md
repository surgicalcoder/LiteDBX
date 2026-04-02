# Phase 6 — Decisions and Subsystem Status

## Overview

Phase 6 targeted shared mode, peripheral subsystems (file readers, rebuild, recovery, upgrade),
system collections with sync assumptions, and utility helpers that blocked threads in async paths.

---

## 1. SharedEngine

**Decision: Supported in-process mode / Cross-process out of scope**

### What changed

- Replaced the blocking named OS `Mutex` + `WaitOne()` with a `SemaphoreSlim(1,1)` whose
  `WaitAsync()` is used in operational paths.
- `SharedEngine` now uses a lease-based shared-session model:
  - the outermost operation acquires the gate and opens the inner `LiteEngine`
  - non-query operations are coordinated through the shared lease helpers
  - shared-mode queries materialize their results under the lease, release the lease,
    and only then yield to the caller
- This removes the shared-mode self-deadlock when callers perform nested operations during
  `await foreach` enumeration (for example `Insert`, `Delete`, `Update`, or nested queries
  inside `FindAll()`).
- `DisposeAsync()` now respects the shared-session lifecycle and only performs immediate
  disposal when there is no active shared session.
- `BeginTransaction` continues to throw `NotSupportedException` with an updated message.

### Final support statement

- `SharedEngine` is a supported async-safe mode for <b>in-process serialized access only</b>.
- It supports nested single-call operations in the same async flow.
- It does <b>not</b> provide cross-process coordination.
- It does <b>not</b> support explicit `ILiteTransaction` scope or transaction-bound operations.
- Consumers that need transaction scope should use direct mode.

### What is deferred

- No additional runtime work is planned for this milestone beyond documenting the boundary clearly.
- Any future cross-process shared-mode design would be a separate feature, not an implied guarantee.

---

## 1a. LockFileEngine

**Decision: Supported limited cross-process mode**

### Final support statement

- `LockFileEngine` is a supported mode for <b>physical file-backed databases</b> that need
  cross-process write coordination.
- It uses short-lived per-call leases around an inner `LiteEngine` and an exclusive lock file.
- It does <b>not</b> support custom streams, `:memory:`, or `:temp:`.
- It does <b>not</b> support explicit `ILiteTransaction` scope or transaction-bound operations.
- Consumers that need transaction scope should use direct mode.

---

## 2. SharedDataReader

**Decision: No change required**

`SharedDataReader` was already correctly redesigned in Phase 4:
- `Read(CancellationToken)` returns `ValueTask<bool>`
- `DisposeAsync()` awaits both the wrapped reader and the dispose callback
- The dispose callback is now invoked via `SemaphoreSlim.Release()` (through `CloseDatabase()`)

Only the Phase 6 status comment was updated.

---

## 3. IFileReader / FileReaderV8 / FileReaderV7

**Decision: IAsyncDisposable added; enumeration methods remain synchronous (documented deferral)**

### What changed

- `IFileReader` now extends both `IDisposable` and `IAsyncDisposable`.
- `FileReaderV8` and `FileReaderV7` both implement `DisposeAsync()` (delegates to sync
  `Dispose(true)` since the underlying streams have no async close).
- The async rebuild path uses `await using` to dispose readers.

### What is deferred

- `Open()`, `GetCollections()`, `GetIndexes()`, `GetDocuments()` remain synchronous.
  These perform bulk sequential reads from a dedicated recovery stream isolated from
  the main engine I/O path. A full async conversion requires streaming through the
  async disk service — scoped to a future phase.

---

## 4. RebuildService

**Decision: Async primary path added; sync path retained only for the constructor**

### What changed

- `RebuildAsync(RebuildOptions, CancellationToken)` is the new primary path. It:
  - Uses `await using` for the reader and temporary engine.
  - Awaits all engine calls: `Pragma`, `Insert`, `Checkpoint`.
  - Calls `await engine.RebuildContentAsync(...)`.
  - Contains no `GetAwaiter().GetResult()` calls.
- `Rebuild(RebuildOptions)` (internal sync overload) is retained **only** for the
  `Recovery()` → `Open()` constructor path. It is explicitly labelled and must not
  be called from any other site.

### What is deferred

- The `Rebuild()` sync overload still uses `GetAwaiter().GetResult()` on the legacy constructor
  path only.

---

## 5. LiteEngine — Rebuild.cs

**Decision: `RebuildContentAsync` added; `Rebuild()` made truly async; `RebuildContent` retained for constructor**

### What changed

- `Rebuild(RebuildOptions, CancellationToken)` now calls `await CloseAsync()` and
  `await rebuilder.RebuildAsync()` — no blocking waits.
- `RebuildContentAsync(IFileReader, CancellationToken)` is truly async:
  - Uses `await _monitor.GetOrCreateTransactionAsync(...)` (not the sync bridge).
  - Uses `await EnsureIndex(...)` (not `GetAwaiter().GetResult()`).
- `RebuildContent(IFileReader)` (sync) is retained for the `RebuildService.Rebuild` /
  constructor path, clearly labelled.

### What is deferred

- `RebuildContent` sync path still uses `GetOrCreateTransactionSync` and
  `EnsureIndex(...).GetAwaiter().GetResult()` — resolved in the remaining startup/recovery
  bridge-removal workstream for the legacy constructor path.

---

## 6. Recovery.cs

**Decision: Primary path migrated; legacy constructor bridge retained**

The explicit `LiteEngine.Open(...)` lifecycle now awaits async recovery via
`RebuildService.RebuildAsync()`. A separate sync `Recovery()` overload is retained only for the
legacy constructor-based startup bridge.

---

## 7. Upgrade.cs — TryUpgrade

**Decision: Primary path migrated; legacy constructor bridge retained**

The explicit `LiteEngine.Open(...)` lifecycle now performs upgrade detection with async file I/O
and awaits recovery when needed. A separate sync `TryUpgrade()` overload is retained only for the
legacy constructor-based startup bridge.

---

## 8. System collections and SysQuery (`$query`)

**Decision: Async input contract added; `$query` re-enabled**

`SystemCollection.Input(...)` now returns `IAsyncEnumerable<BsonDocument>`.

### What changed

- `SystemCollection.Input(...)` is now async-compatible.
- `LiteEngine.Query(...)` defers system-source materialization until after the query transaction
  is established, then buffers the system source once before the synchronous CPU pipeline begins.
- Existing synchronous system collections (`$dump`, `$page_list`, `$file_*`) now adapt through the
  new async input contract without adding sync-over-async bridges.
- `SysQuery` now executes nested SQL via awaited `SqlParser.Execute(...)` and awaited
  `IBsonDataReader.Read(...)`, projecting scalar rows as `{ expr: ... }` documents.

### Remaining limitation

- The core query pipeline is still synchronous after the source-materialization boundary.
  That is acceptable for now because the remaining stages are CPU-bound in-memory transforms.

---

## 9. SysDump / SysIndexes / SysPageList — `GetThreadTransaction()` broken callers

**Decision: Fixed via `AsyncLocal` current-transaction tracker**

Three system collections (`SysDump`, `SysIndexes`, `SysPageList`) still called
`_monitor.GetThreadTransaction()`, which was removed from `TransactionMonitor` in Phase 2.
This left them with unresolved symbol errors.

### What changed

- `TransactionMonitor` gained a static `AsyncLocal<TransactionService> _currentTransaction`
  and two new methods:
  - `SetCurrentTransaction(TransactionService)` — called by `QueryExecutor.ExecuteQueryCore`
    before the pipeline runs (and cleared to `null` in `finally`).
  - `GetCurrentTransaction()` — resolves the running transaction from the explicit ambient
    (`LiteTransaction.CurrentAmbient`) first, then falls back to `_currentTransaction`.
- `GetAmbientTransaction()` is now an alias for `GetCurrentTransaction()`.
- `QueryExecutor.ExecuteQueryCore` calls `SetCurrentTransaction(transaction)` around the
  `RunQuery` pipeline so synchronous system-collection sources running in the same async
  call chain can access the live transaction.
- `SysDump.DumpPages`, `SysIndexes.SysIndexes`, `SysPageList.Input` now call
  `_monitor.GetCurrentTransaction()` instead of the removed `GetThreadTransaction()`.

---

## 10. IOExceptionExtensions — WaitIfLocked

**Decision: Replaced `Task.Delay().Wait()` with `Thread.Sleep()`**

`Task.Delay(...).Wait()` misused the async machinery for a deliberately synchronous
delay inside a sync retry loop (`FileHelper.TryExec` / `FileHelper.Exec`). Replaced
with `Thread.Sleep()`, which is the correct primitive. Blocking the thread in that
context is intentional — these helpers exist for sync file-operation retry in the
rebuild tool path.

---

## Summary: Redesigned vs Deferred

| Subsystem | Status |
|---|---|
| SharedEngine (in-process) | ✅ Redesigned — shared-session lease + buffered query enumeration over `SemaphoreSlim.WaitAsync` |
| SharedEngine (cross-process) | 🔶 Deferred — named OS mutex cross-process coordination |
| SharedDataReader | ✅ Complete (Phase 4, confirmed Phase 6) |
| IFileReader / V7 / V8 | ✅ `IAsyncDisposable` added; enumeration deferred |
| RebuildService async path | ✅ `RebuildAsync` — no `GetAwaiter().GetResult()` |
| RebuildService sync path | 🔶 Deferred — kept for constructor/Recovery only |
| LiteEngine.Rebuild() | ✅ Truly async (`CloseAsync` + `RebuildAsync` + async reopen) |
| RebuildContentAsync | ✅ Truly async (async tx entry, `await EnsureIndex`) |
| RebuildContent (sync) | 🔶 Deferred — constructor Recovery path |
| Recovery() primary path | ✅ Async-native under explicit `LiteEngine.Open(...)` |
| Recovery() constructor bridge | 🔶 Deferred — legacy constructor compatibility path |
| TryUpgrade() primary path | ✅ Async-native under explicit `LiteEngine.Open(...)` |
| TryUpgrade() constructor bridge | 🔶 Deferred — legacy constructor compatibility path |
| WAL restore primary path | ✅ Async-native under explicit `LiteEngine.Open(...)` |
| WAL restore constructor bridge | 🔶 Deferred — legacy constructor compatibility path |
| SystemCollection.Input | ✅ Async-compatible (`IAsyncEnumerable<BsonDocument>`) |
| SysQuery (`$query`) | ✅ Re-enabled on async system-collection input |
| SysDump / SysIndexes / SysPageList | ✅ `GetCurrentTransaction()` replaces removed `GetThreadTransaction()` |
| IOExceptionExtensions | ✅ `Task.Delay().Wait()` → `Thread.Sleep()` |

---

## Risks for the remaining finish work

1. **Legacy constructor bridge removal** — the explicit `LiteEngine.Open(...)` lifecycle is now
   async-native for recovery, upgrade, rebuild reopen, and WAL restore, but constructor-based
   startup still retains sync bridge overloads.
2. **System-source buffering boundary** — system collections are now async-compatible,
   but they are still materialized before the synchronous CPU query pipeline begins.
   A future end-to-end async query pipeline could remove that buffer if needed.
3. **Cross-process SharedEngine** — the original named-mutex pattern is gone. Any
   consumer that relied on cross-process locking will silently lose that guarantee.
   The remaining shared/lock-file support-boundary work must document this in consumer-facing
   release notes and guidance.

