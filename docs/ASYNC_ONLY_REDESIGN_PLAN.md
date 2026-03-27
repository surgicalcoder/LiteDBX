# LiteDB Async-Only Redesign Plan

## Goal

Redesign LiteDB so the entire system is **async by default and async only**:

- No synchronous public API
- No `*Async` suffixes
- No blocking waits for I/O, locks, transactions, or query/result streaming
- Async becomes the only supported execution model

This is not an incremental async enhancement. It is a **breaking architectural redesign** of the client API, engine, transaction model, query pipeline, storage abstractions, and lifecycle.

---

## What “fully async” should mean here

For this redesign, “fully async” should mean:

- CRUD, transactions, queries, checkpointing, rebuilds, and file storage operations are awaitable
- Query streaming uses `IAsyncEnumerable<T>` or an equivalent async cursor abstraction
- Long-lived resources use `IAsyncDisposable`
- File and stream operations are genuinely asynchronous
- Internal coordination uses awaitable primitives instead of blocking synchronization
- The engine does not rely on thread affinity for transaction ownership

### Important nuance

Not every private helper must return `Task`.

Pure CPU-only helpers can remain synchronous internally if they do not:

- block threads
- do I/O
- wait on locks
- interact with async lifecycle boundaries

The requirement should be interpreted as:

- no synchronous public contract
- no synchronous blocking in operational paths
- no synchronous I/O in engine/runtime paths

---

# Summary of Required Changes

## 1. Replace the public API with async-only contracts

### Affected files

- `LiteDB/Client/Database/ILiteDatabase.cs`
- `LiteDB/Client/Database/ILiteCollection.cs`
- `LiteDB/Client/Database/ILiteQueryable.cs`
- `LiteDB/Client/Database/ILiteRepository.cs`
- `LiteDB/Client/Storage/ILiteStorage.cs`
- `LiteDB/Engine/ILiteEngine.cs`
- `LiteDB/Document/DataReader/IBsonDataReader.cs`

### Current problem

The current API surface is synchronous across the board:

- `BeginTrans()`, `Commit()`, `Rollback()`
- `Insert`, `Update`, `Delete`, `Upsert`
- `Find`, `FindById`, `FindOne`, `FindAll`
- `Execute`, `Checkpoint`, `Rebuild`
- query terminal operators like `ToList()`, `First()`, `Count()`
- file storage upload/download/open methods

### Required redesign

Since async is the only model and `Async` suffixes are forbidden, the method names stay plain and the return types change:

- `Insert(...) -> ValueTask<BsonValue>` or `ValueTask<int>`
- `Update(...) -> ValueTask<bool>` or `ValueTask<int>`
- `Delete(...) -> ValueTask<bool>` or `ValueTask<int>`
- `Find(...) -> IAsyncEnumerable<T>`
- `ToList() -> ValueTask<List<T>>`
- `First() -> ValueTask<T>`
- `Count() -> ValueTask<int>`
- `Checkpoint() -> ValueTask`
- `Rebuild(...) -> ValueTask<long>`

### Consequence

This is a **breaking change** for all consumers.

That is acceptable if the goal is a true async-only redesign.

---

## 2. Redesign construction, open, and disposal lifecycle

### Affected files

- `LiteDB/Client/Database/LiteDatabase.cs`
- `LiteDB/Client/Structures/ConnectionString.cs`
- `LiteDB/Engine/LiteEngine.cs`
- `LiteDB/Client/Database/LiteRepository.cs`

### Current problem

The database and engine are synchronously opened in constructors:

- `new LiteDatabase(...)`
- `ConnectionString.CreateEngine()`
- `new LiteEngine(settings)`

Constructors cannot express real async initialization.

Synchronous `Dispose()` also cannot represent async close/flush/checkpoint semantics.

### Required redesign

Move to explicit async lifecycle patterns such as:

- `LiteDatabase.Open(...)`
- async factory/builders
- `IAsyncDisposable` for database/engine/reader/transaction resources

### Why this matters

Open/close paths can involve:

- file open
- WAL recovery
- checkpointing
- encryption setup
- rebuild checks
- pooled resource initialization

Those paths cannot remain hidden inside synchronous constructors and `Dispose()`.

---

## 3. Eliminate thread-bound transaction semantics

### Affected files

- `LiteDB/Engine/Services/TransactionMonitor.cs`
- `LiteDB/Engine/Services/TransactionService.cs`
- `LiteDB/Engine/Services/LockService.cs`
- `LiteDB/Engine/Engine/Transaction.cs`

### Current problem

Transactions are explicitly bound to threads today:

- `ThreadLocal<TransactionService>` in `TransactionMonitor`
- `Environment.CurrentManagedThreadId` in `TransactionService`
- comments/documentation describing transactions as “per-thread”
- transaction ownership and lock release based on current thread

This is incompatible with `await`, because continuations may resume on different threads.

### Required redesign

Replace the current ambient per-thread model with one of the following:

#### Preferred approach: explicit transaction scope objects

Instead of:

- `BeginTrans() -> bool`
- `Commit() -> bool`
- `Rollback() -> bool`

Use:

- `BeginTransaction() -> ValueTask<ILiteTransaction>`

Where `ILiteTransaction` exposes:

- `Commit() -> ValueTask`
- `Rollback() -> ValueTask`
- `DisposeAsync() -> ValueTask`

This is the cleanest and most reliable async model.

#### Alternative approach: ambient async context

Use `AsyncLocal<T>` instead of `ThreadLocal<T>`.

This is possible, but less explicit and easier to misuse.

### Why this matters

Without removing thread affinity, true async transactions are not possible.

---

## 4. Replace blocking locks with async-compatible coordination

### Affected files

- `LiteDB/Engine/Services/LockService.cs`
- `LiteDB/Engine/Sort/SortDisk.cs`
- `LiteDB/Client/Shared/SharedEngine.cs`
- any engine/runtime code using `lock`, `Monitor`, `ReaderWriterLockSlim`, or `Mutex.WaitOne()`

### Current blockers

The codebase currently uses blocking synchronization primitives such as:

- `ReaderWriterLockSlim`
- `Monitor.TryEnter`
- `lock(...)`
- named `Mutex` with `WaitOne()`

These primitives are not suitable for async control flow.

### Required redesign

Use async-compatible primitives:

- `SemaphoreSlim.WaitAsync(...)`
- a custom async reader/writer lock
- async mutex/collection-lock abstractions
- async signaling primitives where needed

### Important note

You cannot safely `await` while depending on ownership semantics from `lock`, `Monitor`, or `ReaderWriterLockSlim`.

So this is not a mechanical replacement. It requires redesigning the critical sections.

---

## 5. Make disk I/O truly asynchronous

### Affected files

- `LiteDB/Engine/Disk/DiskService.cs`
- `LiteDB/Engine/Disk/StreamFactory/IStreamFactory.cs`
- `LiteDB/Engine/Disk/StreamFactory/FileStreamFactory.cs`
- `LiteDB/Engine/Disk/StreamFactory/StreamPool.cs`
- `LiteDB/Engine/Disk/Streams/AesStream.cs`
- `LiteDB/Engine/Disk/Streams/ConcurrentStream.cs`
- `LiteDB/Engine/Disk/Streams/TempStream.cs`
- `LiteDB/Utils/Extensions/StreamExtensions.cs`

### Current problem

Most real storage access is still synchronous:

- WAL writes are synchronous
- direct datafile writes are synchronous
- many file reads are synchronous
- resizing and flush paths are synchronous
- stream wrappers do not override async methods

Even where some async methods already exist, the full path is not async end-to-end.

### Specific issues

#### `DiskService`
Current responsibilities like these are sync-oriented and need async equivalents:

- initialization/open
- full-page reads
- WAL writes
- direct data writes
- file length changes
- invalid-state marking
- close/flush/checkpoint-related disk activity

#### `FileStreamFactory`
File streams need to be opened with async usage as the default assumption.

#### Wrapper streams
These wrappers currently expose sync behavior and do not fully implement async operations:

- `AesStream`
- `ConcurrentStream`
- `TempStream`
- `LiteFileStream<TFileId>`

If a wrapper does not override `ReadAsync`, `WriteAsync`, and `FlushAsync`, then async calls may degrade into sync execution.

### Required redesign

- Async-first stream creation
- Async read/write/flush throughout the disk path
- Async stream wrappers
- Async file resize and checkpoint persistence
- Async disposal for pooled stream resources where needed

---

## 6. Redesign query execution as async streaming

### Affected files

- `LiteDB/Client/Database/LiteQueryable.cs`
- `LiteDB/Client/Database/Collections/Find.cs`
- `LiteDB/Document/DataReader/IBsonDataReader.cs`
- `LiteDB/Document/DataReader/BsonDataReader.cs`
- `LiteDB/Document/DataReader/BsonDataReaderExtensions.cs`
- `LiteDB/Client/Shared/SharedDataReader.cs`
- `LiteDB/Engine/Query/QueryExecutor.cs`
- pipeline code under `LiteDB/Engine/Query/**`

### Current problem

The query pipeline is built on synchronous pull-based iteration:

- `IEnumerable<T>`
- `IEnumerator<T>`
- `IBsonDataReader.Read()`
- sync terminal operations like `ToList()`, `First()`, `Single()`

This architecture is fundamentally synchronous.

### Required redesign

#### Preferred direction: `IAsyncEnumerable<T>`
Use async streams as the primary result model.

That means:

- query builders remain cheap and synchronous to compose
- terminal execution yields `IAsyncEnumerable<T>`
- materializers become async-returning

#### If keeping a reader abstraction
If `IBsonDataReader` survives, it should become async:

- `Read() -> ValueTask<bool>`
- `DisposeAsync() -> ValueTask`
- `Current` can remain property-based

But if the system is being redesigned fully, `IAsyncEnumerable<T>` is simpler and more idiomatic.

### Query API target shape

Builder methods remain synchronous:

- `Where(...)`
- `OrderBy(...)`
- `Include(...)`
- `GroupBy(...)`

Terminal operations become async-only:

- `ToEnumerable() -> IAsyncEnumerable<T>`
- `ToList() -> ValueTask<List<T>>`
- `First() -> ValueTask<T>`
- `Single() -> ValueTask<T>`
- `Count() -> ValueTask<int>`

No `Async` suffixes, because async is the default.

---

## 7. Replace `Stream`-based file storage if zero sync surface is required

### Affected files

- `LiteDB/Client/Storage/LiteFileStream.cs`
- `LiteDB/Client/Storage/LiteFileStream.Read.cs`
- `LiteDB/Client/Storage/LiteFileStream.Write.cs`
- `LiteDB/Client/Storage/LiteStorage.cs`
- `LiteDB/Client/Storage/ILiteStorage.cs`

### Current problem

`LiteFileStream<TFileId>` inherits `Stream`.

`Stream` includes synchronous methods by design:

- `Read`
- `Write`
- `Flush`
- `Seek`
- `CopyTo`

If the requirement is literally “no sync in there,” then continuing to expose `Stream` violates that goal even if async methods are also implemented.

### Required redesign

Replace `LiteFileStream<TFileId>` with an async-only file handle abstraction.

Examples of possible abstractions:

- async read handle
- async write session
- async file/blob cursor
- chunked async reader/writer interface

### Also required

These storage operations become async-only:

- `OpenRead(...)`
- `OpenWrite(...)`
- `Upload(...)`
- `Download(...)`
- metadata update operations if they touch storage

And any sync file-copy logic must be replaced:

- `Stream.CopyTo(...)`
- `File.OpenRead(...)`
- sync file save/load helpers

---

## 8. `SharedEngine` requires redesign or postponement

### Affected file

- `LiteDB/Client/Shared/SharedEngine.cs`

### Current problem

`SharedEngine` currently relies on:

- named `Mutex`
- `WaitOne()`
- synchronous open-per-call/close-per-call behavior
- sync critical sections

This does not map cleanly to an async-only architecture.

### Options

#### Option A: redesign shared mode
Possible alternatives:

- file-lock based async coordination
- process-level broker/arbiter
- long-lived shared coordinator with serialized async access
- another cross-process strategy that does not depend on blocking named mutex waits

#### Option B: defer shared mode
If the goal is to ship a clean async-only foundation first, shared mode may need to be temporarily dropped or postponed.

### Recommendation

Treat `SharedEngine` as a separate redesign track. It is one of the hardest pieces in the solution to make truly async.

---

## 9. Propagate async through engine operations instead of wrapping sync code

### Affected files

- `LiteDB/Engine/Engine/Insert.cs`
- `LiteDB/Engine/Engine/Update.cs`
- `LiteDB/Engine/Engine/Delete.cs`
- `LiteDB/Engine/Engine/Upsert.cs`
- `LiteDB/Engine/Engine/Query.cs`
- `LiteDB/Engine/Engine/Transaction.cs`
- `LiteDB/Engine/LiteEngine.cs`

### Current problem

The engine core is synchronous and transaction execution is sync-based.

### Required redesign

The engine entry points themselves must become async-returning and propagate `await` down into:

- transaction acquisition
- snapshot creation
- query execution
- page persistence
- WAL writes
- checkpointing
- rebuild flows

### Important warning

Do **not** wrap synchronous engine work in `Task.Run` and call that “async.”

That would produce:

- blocked worker threads
- poor scalability
- fake async semantics
- harder debugging

The redesign must make the operational path itself asynchronous.

---

## 10. Replace `IDisposable` with `IAsyncDisposable` where resource cleanup can await

### Affected areas

- `ILiteDatabase`
- `ILiteEngine`
- query readers/cursors
- transaction scopes
- stream pools/disk services
- file handles
- any component whose close path may flush or release async resources

### Required redesign

Use:

- `IAsyncDisposable`
- `await using`

### Why this matters

Async close paths may need to:

- flush WAL
- release async locks
- dispose pooled async streams
- checkpoint before shutdown
- release async enumerators/cursors cleanly

Synchronous disposal is not sufficient for that design.

---

## 11. Update wrappers, tools, tests, and consumers

### Affected projects

- `LiteDB.Tests`
- `LiteDB.Shell`
- `LiteDB.Benchmarks`
- `LiteDB.Stress`
- `ConsoleApp1`

### Current problem

All current consumers assume the sync API surface.

Examples include:

- `.Insert(...)`
- `.Commit()`
- `.ToList()`
- `.First()`
- `.Execute(...)`
- `.Upload(...)`

### Required redesign

All usages must become await-based or async-stream based.

This includes:

- tests
- sample apps
- shell commands
- stress tools
- benchmark drivers

---

# Recommended Target Architecture

## Public API

Keep the plain method names, but make them async-returning:

- `Insert`
- `Update`
- `Delete`
- `Find`
- `First`
- `Count`
- `Commit`

Use return types such as:

- `ValueTask<T>`
- `Task<T>`
- `IAsyncEnumerable<T>`

## Query model

### Composition methods remain synchronous

These should remain cheap builders:

- `Where`
- `OrderBy`
- `Include`
- `GroupBy`
- `Having`
- `Select`

### Execution methods become async-only

- `ToEnumerable() -> IAsyncEnumerable<T>`
- `ToList() -> ValueTask<List<T>>`
- `First() -> ValueTask<T>`
- `Single() -> ValueTask<T>`
- `Count() -> ValueTask<int>`

## Transactions

Preferred model:

- `BeginTransaction() -> ValueTask<ILiteTransaction>`

Where the transaction is an explicit async scope instead of ambient thread-bound state.

## File storage

Replace `Stream` inheritance with an async-only file abstraction if the goal is truly zero sync surface.

## Lifetime

- Async open via factory/static methods
- Async dispose via `IAsyncDisposable`

---

# Migration Order

## Phase 1 — Define the async-only contract

Redesign and freeze the target shapes for:

- `ILiteEngine`
- `ILiteDatabase`
- `ILiteCollection<T>`
- `ILiteQueryable<T>`
- `ILiteStorage<TFileId>`
- `ILiteRepository`
- query/result abstractions
- transaction abstractions

This must come first so the rest of the work has a stable destination.

---

## Phase 2 — Redesign transaction ownership and locking

Refactor:

- `TransactionMonitor`
- `TransactionService`
- `LockService`
- engine transaction orchestration

This is the foundational blocker for async correctness.

---

## Phase 3 — Make disk and stream infrastructure async-first

Refactor:

- `DiskService`
- `IStreamFactory`
- `FileStreamFactory`
- `StreamPool`
- custom stream wrappers
- WAL persistence and flush paths
- initialization and close paths

---

## Phase 4 — Convert the query pipeline to async streaming

Refactor:

- `QueryExecutor`
- `BsonDataReader` or replace it
- pipeline components under `LiteDB/Engine/Query`
- `LiteQueryable<T>`
- collection query entry points

---

## Phase 5 — Redesign file storage

Refactor:

- `LiteStorage<TFileId>`
- `LiteFileStream<TFileId>` replacement
- upload/download/open flows
- chunk read/write pipeline

---

## Phase 6 — Handle shared mode and peripheral subsystems

Refactor or defer:

- `SharedEngine`
- rebuild paths
- file readers
- export/import helpers
- system collection helpers that assume sync flow

---

## Phase 7 — Update all tests and downstream consumers

Update:

- tests
- shell
- benchmarks
- stress tools
- sample apps

Only after this phase can the redesign be validated comprehensively.

---

# Biggest Architectural Blockers

If this project were prioritized by risk, the top blockers are:

## 1. Thread-affine transactions

- `ThreadLocal<TransactionService>`
- `Environment.CurrentManagedThreadId`
- “per-thread” transaction model

## 2. Blocking synchronization

- `ReaderWriterLockSlim`
- `Monitor`
- `lock`
- `Mutex.WaitOne()`

## 3. Constructor/dispose lifecycle

- sync open in constructors
- sync close/dispose

## 4. Sync query/result model

- `IEnumerable<T>`
- `IEnumerator<T>`
- `IBsonDataReader.Read()`

## 5. `Stream`-based file storage

- `LiteFileStream<TFileId> : Stream`
- sync `Read/Write/Flush/Seek`

## 6. Wrapper streams lacking async overrides

- `AesStream`
- `ConcurrentStream`
- `TempStream`
- `LiteFileStream`

## 7. Shared mode design

- named mutex model is sync/blocking
- open/close per operation does not fit async-first architecture well

---

# Practical Recommendation

This should be treated as:

- **LiteDB vNext**
- **major breaking release**
- **async-only contract**
- likely a staged rollout with `SharedEngine` handled separately

Trying to preserve full backward compatibility while also removing all sync paths will make the design substantially worse.

A clean break is more realistic.

---

# Bottom Line

To satisfy the stated requirement exactly, LiteDB must be redesigned so that:

- all public operations are async-only
- async is the default and only calling convention
- synchronous blocking primitives are removed from operational paths
- thread-bound transactions are replaced
- disk and query pipelines become truly async
- `Stream`-based storage is replaced if zero sync surface is required
- creation and disposal become async-aware
- shared mode is redesigned or deferred

This is a **full contract and runtime model rewrite**, not a surface-level refactor.

---

# Suggested Next Document

After this file, the next most useful deliverable would be a **file-by-file implementation roadmap** that defines the exact proposed contract shapes for:

- `ILiteDatabase`
- `ILiteCollection<T>`
- `ILiteQueryable<T>`
- `ILiteRepository`
- `ILiteStorage<TFileId>`
- `ILiteEngine`
- transaction abstractions
- async file abstractions

That would turn this design plan into an execution plan.
