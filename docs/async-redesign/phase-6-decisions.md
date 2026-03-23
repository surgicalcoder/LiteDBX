# Phase 6 — Decisions and Subsystem Status

## Overview

Phase 6 targeted shared mode, peripheral subsystems (file readers, rebuild, recovery, upgrade),
system collections with sync assumptions, and utility helpers that blocked threads in async paths.

---

## 1. SharedEngine

**Decision: Redesigned (in-process) / Cross-process deferred**

### What changed

- Replaced the blocking named OS `Mutex` + `WaitOne()` with a `SemaphoreSlim(1,1)` whose
  `WaitAsync()` is used in all operational paths.
- `OpenDatabaseAsync()` acquires the semaphore without blocking any thread.
- `QueryDatabaseAsync<T>` and `QueryStream` now `await OpenDatabaseAsync()`.
- `DisposeAsync()` properly awaits the inner `LiteEngine.DisposeAsync()` before disposing
  the semaphore.
- `BeginTransaction` continues to throw `NotSupportedException` with an improved message.

### What is deferred

- **Cross-process exclusive file coordination** (the original named-mutex purpose) is
  explicitly deferred. No hidden sync fallback exists. A future phase may introduce
  async-safe cross-process coordination (e.g. polling file-lock with `Task.Delay` retries).
- **Explicit transaction scope** across multiple per-call open/close cycles is deferred;
  the per-call lifecycle model does not support it without a deeper redesign.

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

- The `Rebuild()` sync overload still uses `GetAwaiter().GetResult()` on the constructor
  path. This is resolved once Phase 7 introduces `LiteEngine.OpenAsync()` (async factory).

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

- `Open()` after `Rebuild` is still synchronous (constructor limitation).
- `RebuildContent` sync path still uses `GetOrCreateTransactionSync` and
  `EnsureIndex(...).GetAwaiter().GetResult()` — resolved with Phase 7 async factory.

---

## 6. Recovery.cs

**Decision: Documented deferral**

`Recovery()` is called synchronously from `Open()` (constructor), so it cannot be made
async without a static `OpenAsync()` factory. Updated documentation explains this clearly.
The method correctly delegates to the internal sync `RebuildService.Rebuild()` overload.

---

## 7. Upgrade.cs — TryUpgrade

**Decision: Documented deferral**

`TryUpgrade()` runs synchronously during `Open()` (constructor path). The file detection
read and the `Recovery()` call both block the calling thread. Updated documentation
explains the deferral. No functional change needed — the sync file read for version
detection is acceptable for a startup one-shot check.

---

## 8. SysQuery (`$query`)

**Decision: Explicitly disabled**

`SysQuery.Input()` now throws `NotSupportedException` with a clear explanation.

**Reason:** `SqlParser.Execute()` returns `ValueTask<IBsonDataReader>` and
`IBsonDataReader.Read()` returns `ValueTask<bool>`, but `SystemCollection.Input()`
must return `IEnumerable<BsonDocument>`. Driving an async reader from a sync
`IEnumerable` producer would require `GetAwaiter().GetResult()` inside a `yield return`
loop — which is not possible in a `yield`-based iterator and would be wrong anyway.

**Deferred to Phase 7:** Migrate `SystemCollection.Input()` to return
`IAsyncEnumerable<BsonDocument>` and update all callers in the query pipeline, then
re-enable `$query`.

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
| SharedEngine (in-process) | ✅ Redesigned — `SemaphoreSlim.WaitAsync` |
| SharedEngine (cross-process) | 🔶 Deferred — named OS mutex cross-process coordination |
| SharedDataReader | ✅ Complete (Phase 4, confirmed Phase 6) |
| IFileReader / V7 / V8 | ✅ `IAsyncDisposable` added; enumeration deferred |
| RebuildService async path | ✅ `RebuildAsync` — no `GetAwaiter().GetResult()` |
| RebuildService sync path | 🔶 Deferred — kept for constructor/Recovery only |
| LiteEngine.Rebuild() | ✅ Truly async (`CloseAsync` + `RebuildAsync`) |
| RebuildContentAsync | ✅ Truly async (async tx entry, `await EnsureIndex`) |
| RebuildContent (sync) | 🔶 Deferred — constructor Recovery path |
| Recovery() | 🔶 Deferred — constructor async factory (Phase 7) |
| TryUpgrade() | 🔶 Deferred — constructor async factory (Phase 7) |
| SysQuery (`$query`) | 🚫 Disabled — `NotSupportedException` (Phase 7 re-enable) |
| SysDump / SysIndexes / SysPageList | ✅ `GetCurrentTransaction()` replaces removed `GetThreadTransaction()` |
| IOExceptionExtensions | ✅ `Task.Delay().Wait()` → `Thread.Sleep()` |

---

## Risks for Phase 7

1. **Async constructor factory** — `LiteEngine.OpenAsync()` is the key prerequisite for
   resolving the `Recovery()`, `TryUpgrade()`, and `RebuildService.Rebuild()` sync
   fallback paths. This must be the first item in Phase 7.
2. **SysQuery re-enablement** requires `SystemCollection.Input()` to return
   `IAsyncEnumerable<BsonDocument>`. Changing this interface touches all system
   collections and the query pipeline's `source` parameter — a non-trivial diff.
3. **Cross-process SharedEngine** — the original named-mutex pattern is gone. Any
   consumer that relied on cross-process locking will silently lose that guarantee.
   Phase 7 must document this in consumer-facing release notes.

