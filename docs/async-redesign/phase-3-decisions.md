# Phase 3 — Disk and Stream Infrastructure Decisions

_Records key design choices made during Phase 3 of the async-only LiteDB redesign._

---

## 1. `FileOptions.Asynchronous` on all `FileStream` instances

**Decision:** Every `FileStream` opened by `FileStreamFactory` (and by `FileStreamFactory.GetLength`
when it trims a misaligned file) now includes `FileOptions.Asynchronous`.

**Rationale:**
On Windows, `FileOptions.Asynchronous` binds the file handle to the I/O Completion Port (IOCP)
thread pool. Without this flag, `ReadAsync`/`WriteAsync` on a `FileStream` fall back to a
synchronous thread-pool dispatch that blocks a thread waiting for the OS call to return.
On Linux (io_uring / epoll) the flag has no negative effect.

**Buffer size:** The page-size buffer hint passed to the `FileStream` constructor is `PAGE_SIZE`
(8 KB). This matches the LiteDB page boundary and avoids the OS performing extra copy-on-write
operations for async scatter-gather I/O.

**Platform note — `FileStream` construction is always synchronous:**
`GetStreamAsync` (on both `FileStreamFactory` and `StreamFactory`) completes synchronously on all
target runtimes because the BCL provides no async `FileStream` constructor. The async benefit is
entirely in subsequent `ReadAsync`/`WriteAsync` calls issued on the returned stream.

---

## 2. `ConcurrentStream` — `lock` replaced with `SemaphoreSlim`

**Decision:** `ConcurrentStream` wraps the shared in-memory/temp stream with a `SemaphoreSlim`
gate instead of a `lock` statement, and the gate is shared across all `ConcurrentStream` instances
wrapping the same underlying `Stream`.

**Rationale:**
`lock` is incompatible with `await` expressions inside the locked region. Even if no `await` is
currently inside the lock, starting async I/O on a stream protected only by `lock` violates
async hygiene — the lock is released before the async operation completes.

**Shared gate:** `StreamFactory` owns a single `SemaphoreSlim _streamGate` and passes it to
every `ConcurrentStream` it creates. This ensures mutual exclusion is maintained across all
concurrent stream users that share the same underlying `MemoryStream` / `TempStream`.

---

## 3. `SemaphoreSlim` in `DiskService.WriteLogDisk`

**Decision:** The former `lock(stream)` block around WAL writes is replaced with a
`SemaphoreSlim(1,1)` named `_writerGate`. The async path uses `WaitAsync`; the sync bridge uses
`Wait`.

**Rationale:** The WAL writer is the single most contended synchronisation point in the engine.
Blocking threads here under high-concurrency workloads causes thread-pool starvation. The
`SemaphoreSlim` allows waiting callers to yield back to the async scheduler rather than spinning
or blocking.

---

## 4. `AesStream` async semantics and platform constraints

**`CryptoStream` async on netstandard2.0:**
`CryptoStream.ReadAsync`/`WriteAsync` dispatch async I/O on .NET Core 2.1+ but still run
synchronously on netstandard2.0 **at the crypto transform layer**. The underlying `FileStream`
I/O is async either way when `FileOptions.Asynchronous` is set — only the encrypt/decrypt
transform itself is synchronous on netstandard2.0.

**Decision:** The async overrides are implemented unconditionally. On netstandard2.0 targets the
calls are still awaitable and correct; they do not deliver a fully async transform pipeline but
the OS-level I/O beneath them remains non-blocking. This is acceptable because LiteDB's primary
supported runtime is .NET 6+.

**AES constructor:** The `AesStream` constructor (password verification, salt handling) is
synchronous. It reads and writes a small number of bytes at open time. This is a startup-only
sync block and is deferred to the remaining lifecycle/open cleanup workstream tracked in
`docs/async-redesign/REMAINING_WORK_IMPLEMENTATION_PLAN.md`.

---

## 5. Flush durability constraints

**`FlushToDiskAsync` semantics:**
`FlushToDiskAsync` calls `stream.FlushAsync(ct)`. On .NET 6+ with `FileOptions.Asynchronous`,
this reaches physical storage (the OS issues an `fsync`/`FlushFileBuffers` equivalent as part of
the async flush path). On older runtimes (netstandard2.0, .NET Framework), `FlushAsync` only
flushes to the OS kernel buffer cache and does **not** guarantee physical-disk durability.

**Hard durability on older platforms:**
Hard durability on older platforms still requires the synchronous `Flush(true)` path, which is
used on the shutdown/startup bridges (`Initialize`, `FileStreamFactory.GetLength`). These are
startup-only operations where a brief sync call is acceptable and documented.

**Hot-path WAL writes:**
`WriteLogDisk` does **not** call `FlushToDiskAsync` after each page batch. Durability is provided
at checkpoint time, consistent with WAL semantics. Only `WriteDataDisk` (the checkpoint data-file
write) and `MarkAsInvalidStateAsync` (the abnormal-close path) call `FlushToDiskAsync`.

---

## 6. Startup bootstrap — `DiskService` constructor and WAL restore

**Decision:** The explicit `LiteEngine.Open(...)` lifecycle now uses async WAL restore, but
`DiskService` construction/initialization remains partially synchronous and the constructor-based
legacy startup path still uses the sync `RestoreIndex(ref HeaderPage)` bridge.

**Rationale:**
Startup orchestration is now on the explicit `LiteEngine.Open(...)` lifecycle for the primary
supported path, which allows WAL restore to use `ReadFullAsync`. The remaining sync bootstrap is
isolated to `DiskService` construction/initialization and the legacy constructor path.

---

## 7. `SortService.Insert` — Phase 4 sync bridge for `SortDisk.WriteSync`

**Decision:** `SortDisk.Write` was made async (`ValueTask`) in Phase 3. `SortService.Insert` is
a synchronous method that remains a Phase 4 bridge. It calls `SortDisk.WriteSync` (the sync
bridge method) rather than the new async `Write`.

**Rationale:** `SortService.Insert` is called from the query pipeline (`BasePipe`, `QueryPipe`,
`GroupByPipe`) which is also still synchronous. Converting `Insert` to async requires Phase 4
(query pipeline redesign). Until that phase the sync bridge is the correct call.

**Risk mitigated:** Calling `_disk.Write(...)` (the async `ValueTask` overload) from sync code
without awaiting silently discards the task (CS4014 warning) — the sort container data may not
be on disk before `container.InitializeReader` runs. The call site in `SortService.Insert` is
fixed to use `WriteSync` to make the intent explicit and correct.

---

## 8. `DiskReader.ReadPage` sync bridge

**Decision:** `DiskReader.ReadPage` (synchronous) is kept alongside the new `ReadPageAsync`.

**Rationale:** `Snapshot` and `QueryExecutor` still call `ReadPage` synchronously. Phase 4
(query pipeline) will convert these callers. The sync bridge is annotated to direct Phase 4
authors.

---

## 9. `TempStream` async spill semantics

**Decision:** `TempStream.WriteAsync` checks the spill threshold before writing. If the current
position plus the write count would exceed `_maxMemoryUsage` and the stream is still in memory,
it calls `SpillToFileAsync` which uses `CopyToAsync` to copy the in-memory content to a real
`FileStream` opened with `FileOptions.Asynchronous | FileOptions.RandomAccess`.

**Note:** The sync `Seek` path calls the sync `SpillToFile` overload. This is acceptable because
`Seek` is position-setting only and is not in the hot I/O path.

---

## 10. Deferred items

| Item | Reason deferred | Remaining workstream |
|---|---|---|
| `LiteEngine` explicit open lifecycle cleanup | Supported `LiteEngine.Open(...)` now exists, but constructor-driven startup remains transitional in some paths | Lifecycle/open cleanup |
| `DiskService` startup initialization async factory | `Initialize(...)`, `GetLength()`, and related bootstrap work are still synchronous in the current disk-service construction flow | Startup/recovery bridge removal |
| `DiskReader.ReadPage` callers converted to async | Part of query pipeline async conversion | Phase 4 |
| `TransactionService.Safepoint()` async version | Called from sync query pipeline | Phase 4 |
| Test consumer `Checkpoint()` awaiting | Tests not yet converted to async | Phase 7 |
| Hard-durable `FlushAsync(flushToDisk:true)` | BCL has no such overload; sync `Flush(true)` used on startup/shutdown bridges only | Accepted limitation |
