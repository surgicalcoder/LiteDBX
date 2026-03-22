# Phase 5 Decisions — File Storage Redesign

## Context

Phase 5 refactored the LiteDB file storage subsystem to be fully async-only and to eliminate
the last public synchronous surface area: `LiteFileStream<TFileId> : Stream`.

---

## Decision 1 — Remove `Stream` inheritance from the public file handle

**Question:** Should `LiteFileStream<TFileId>` keep its `System.IO.Stream` base class?

**Decision:** No. `Stream` is removed from the public surface entirely.

**Rationale:**
`System.IO.Stream` is an abstract class whose contract includes three obligatory synchronous
abstract members: `Read(byte[], int, int)`, `Write(byte[], int, int)`, and `Flush()`.
Any class that inherits `Stream` must implement those members. Since the async-only rule
forbids a synchronous public API, a class that inherits `Stream` cannot be made
truly async-only — it will always expose synchronous entry points.

**Replacement:** `ILiteFileHandle<TFileId>` (introduced in Phase 1 planning, implemented
in Phase 5). It exposes only:
- `ValueTask<int> Read(Memory<byte>, CancellationToken)`
- `ValueTask Write(ReadOnlyMemory<byte>, CancellationToken)`
- `ValueTask Flush(CancellationToken)`
- `ValueTask Seek(long, CancellationToken)`
- `IAsyncDisposable` (DisposeAsync)

---

## Decision 2 — Seek semantics on read handles

**Question:** Should `Seek` on a read handle be a synchronous state change or a genuine
async operation that prefetches the target chunk?

**Decision:** `Seek` is a pure position-state change. It returns `default(ValueTask)` (a
completed `ValueTask`) without doing any I/O. The chunk at the new position is loaded
lazily on the next `Read()` call.

**Rationale:**
- Seek itself involves no I/O — it is a pure integer arithmetic operation on position.
- Eager chunk prefetch on every seek would be wasteful for callers who seek and then
  immediately seek again (e.g., binary search over chunks).
- The public signature returns `ValueTask` so the contract is async-compatible for
  any future implementation that needs real async seek (e.g., remote storage back-ends).
- Returning `default(ValueTask)` is correct on all target frameworks including netstandard2.0.

**Seek is NOT supported on write handles.** Throws `NotSupportedException`. Write handles
are sequential-append only, consistent with the original `LiteFileStream` write mode.

---

## Decision 3 — Chunk deletion before overwrite is performed in `OpenWrite`

**Question:** Where should existing chunk deletion happen when a file is overwritten —
inside the write handle, or before the write handle is created?

**Decision:** In `LiteStorage<TFileId>.OpenWrite`, before `LiteFileHandle<TFileId>.CreateWriter`
is called.

**Rationale:**
- `LiteFileHandle` is a simple write session that assumes a clean slate (no pre-existing chunks).
- Separating deletion from the handle keeps `LiteFileHandle` simple and testable.
- It avoids the need for the handle to hold overwrite state.
- The constructor of `LiteFileHandle` is synchronous; async chunk deletion in the constructor
  would require a factory method pattern. `OpenWrite` is already a `ValueTask`-returning factory,
  so it is the natural place for async pre-work.

---

## Decision 4 — `LiteFileInfo<TFileId>` becomes a POCO

**Question:** Should `LiteFileInfo<TFileId>` keep internal collection references and sync
helper methods (`OpenRead`, `OpenWrite`, `CopyTo`, `SaveAs`)?

**Decision:** No. `LiteFileInfo<TFileId>` is now a pure data transfer object with no
collection references and no methods beyond property accessors.

**Rationale:**
- The former helpers created a hidden dependency from a data object into the storage layer.
- The sync methods (`CopyTo`, `SaveAs`) used `Stream.CopyTo` and `File.Open`, both synchronous.
- All operations are now exposed as async methods on `ILiteStorage<TFileId>` and
  `ILiteFileHandle<TFileId>` — the correct home for them.

---

## Decision 5 — Upload / Download helpers copy via async byte-array pipe

**Question:** How should `Upload(Stream)` and `Download(Stream)` copy data?

**Decision:** Both helpers use a `byte[]` buffer (sized to `MaxChunkSize = 255 KB`) and
loop with `stream.ReadAsync` / `stream.WriteAsync`. No `Stream.CopyTo` is used.

**Rationale:**
- `Stream.CopyTo` is synchronous and violates the async-only rule.
- The buffer-and-loop pattern mirrors `Stream.CopyToAsync` but is explicit about the
  chunk size, which keeps memory usage bounded and consistent with the chunk layout.

The `Upload(string filename)` file-system overload opens the source as a `FileStream`
with `useAsync: true` and passes it to the stream-based `Upload` path.

---

## Decision 6 — `DisposeAsync` as safety flush for write handles

**Decision:** `LiteFileHandle<TFileId>.DisposeAsync` calls `PersistBufferedChunks(finalize: true)`
if `Flush` has not already been called. This ensures that a write session abandoned without
an explicit `Flush` still commits its buffered data.

**Rationale:**
- Consistent with `LiteFileStream<TFileId>.Dispose` in the old sync code.
- Callers using `await using` get automatic commit semantics without needing to call `Flush`.
- `_finalized` guard prevents double-commit if `Flush` was already called.

---

## Public storage model — summary

| Concern                   | Old (sync)                              | New (async-only, Phase 5)                         |
|---------------------------|-----------------------------------------|---------------------------------------------------|
| Public handle type        | `LiteFileStream<TFileId> : Stream`      | `ILiteFileHandle<TFileId> : IAsyncDisposable`     |
| Read                      | `Stream.Read(byte[], int, int)`         | `ValueTask<int> Read(Memory<byte>, CT)`           |
| Write                     | `Stream.Write(byte[], int, int)`        | `ValueTask Write(ReadOnlyMemory<byte>, CT)`       |
| Flush                     | `Stream.Flush()`                        | `ValueTask Flush(CT)`                             |
| Seek (read only)          | `Stream.Seek(long, SeekOrigin)`         | `ValueTask Seek(long, CT)` — pure state change    |
| Seek on write             | `NotSupportedException`                 | `NotSupportedException`                           |
| Finalization              | `Stream.Dispose(bool)` — sync           | `DisposeAsync()` — async, with safety flush       |
| Upload from stream        | `Stream.CopyTo(writer)`                 | `ReadAsync` / `Write` loop                        |
| Download to stream        | `Stream.CopyTo(target)`                 | `Read` / `WriteAsync` loop                        |
| File info helpers         | `OpenRead`, `OpenWrite`, `CopyTo`, `SaveAs` on `LiteFileInfo` | Removed — use `ILiteStorage` |
| `LiteFileInfo` type       | DTO + injected collection refs + methods | Pure DTO, no collection refs                     |

---

## Deferred items

None. All work items from `05-phase-5-file-storage.md` are implemented.

The namespace warning (`LiteDbX` vs `LiteDbX.Client.Storage`) shown in IDE analysis for
`LiteStorage.cs` and `LiteFileHandle.cs` is not an error and is consistent with the naming
convention used throughout the entire `LiteDbX` project. It is intentionally left as-is.

