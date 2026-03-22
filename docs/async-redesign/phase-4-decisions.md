# Phase 4 — Query Pipeline: Design Decisions and Lifecycle Notes

## Result Abstraction

**Chosen model**: `IAsyncEnumerable<BsonDocument>` as the primary public streaming result type,
backed by an `IBsonDataReader` async cursor for the SQL-level `Execute` path.

### Why `IBsonDataReader` is retained

`IBsonDataReader` is kept (not replaced entirely by a raw `IAsyncEnumerable`) because:
- It carries `Collection` context required by SQL-level `Execute` callers.
- It supports indexed field access via `this[string field]`.
- It maps cleanly to the cursor-based reader pattern familiar to database client consumers.

Callers who want a plain stream use `BsonDataReaderExtensions.ToAsyncEnumerable(reader)`.
`ILiteQueryable<T>` exposes `ToEnumerable(ct)` and `ToDocuments(ct)` which return
`IAsyncEnumerable<T>` directly from the engine without going through a cursor.

## Disposal Model

| Instance type | Disposal |
|---|---|
| Empty `BsonDataReader` | no-op `ValueTask` |
| Single-value `BsonDataReader` | no-op `ValueTask` |
| Stream `BsonDataReader` | awaits `IAsyncEnumerator<BsonDocument>.DisposeAsync()`, which triggers the `finally` in `QueryExecutor.ExecuteQueryCore`, which removes the cursor and releases the transaction. |
| `SharedDataReader` | awaits inner reader `DisposeAsync()`, then awaits the registered dispose callback. |

## Cursor Lifetime

1. `LiteEngine.Query()` validates arguments eagerly (before entering the async iterator).
2. `QueryCore` creates a `QueryExecutor` and delegates to `ExecuteQuery(ct)`.
3. `ExecuteQueryCore` acquires a transaction via `GetOrCreateTransactionAsync` (non-blocking).
4. The cursor is registered on `transaction.OpenCursors` for the lifetime of enumeration.
5. The `finally` block (triggered by normal completion, early break, cancellation, or caller
   disposal) removes the cursor and — if the transaction was auto-created — releases it.
6. Breaking out of an `await foreach` disposes the enumerator, which propagates to the
   `BsonDataReader`'s enumerator, which triggers step 5.

## Synchronous Internal Transforms

The following pipeline components remain synchronous because they are pure CPU-bound transforms
over the in-memory page cache. No thread is blocked on I/O; only the transaction lock entry
(step 3 above) is a genuine async boundary.

- `BasePipe.LoadDocument` — page-cache pointer dereference
- `BasePipe.Filter` — expression evaluation
- `BasePipe.Include` — snapshot read (snapshot holds pages in memory)
- `BasePipe.OrderBy` — sort service (uses temp disk via `SortDisk.WriteSync` — Phase 3 deferred)
- `QueryPipe.Select` / `QueryPipe.SelectAll` — expression evaluation
- `GroupByPipe.GroupBy` / `GroupByPipe.SelectGroupBy` — grouping + aggregation

### WAL read-lock deferred item

`WalIndexService.GetCurrentReadVersion` still calls `_indexLock.EnterReadSync` inside
`TransactionService.CreateSnapshot`. Making this await-based is a Phase 3 / Phase 6 deferred
item. At the transaction-per-query level, the write lock entry is already async
(`GetOrCreateTransactionAsync` / `LockService.EnterTransactionAsync`), so the per-snapshot
WAL read lock is the only remaining sync wait in the operational query path.

## `LiteQueryable` Aggregate Pattern

`Count`, `LongCount`, and `Exists` temporarily mutate `_query.Select` with an aggregation
expression, execute the engine query, then restore the original `Select`. The save/restore
pattern is retained from the pre-Phase 4 implementation. Concurrent use of the same
`LiteQueryable` instance is not supported (pre-existing limitation).

## SQL-level Transaction Commands (BEGIN / COMMIT / ROLLBACK)

SQL-level transaction commands were removed. `SqlParser.Execute` throws
`NotSupportedException` for `BEGIN`, `COMMIT`, and `ROLLBACK`.

**Reason**: The new transaction model uses `ILiteTransaction` scope objects (set via
`AsyncLocal`) that must persist across multiple `Execute()` calls. The single-call
`Execute() → IBsonDataReader` contract cannot carry this state without a significant
`SqlParser` redesign (which is Phase 6 scope).

**Migration**: Use `ILiteDatabase.BeginTransaction()` and the returned `ILiteTransaction`
(`Commit()`, `Rollback()`, `DisposeAsync()`) from application code.

## `SharedEngine` — Phase 6 Deferred Items

- `Mutex.WaitOne()` blocks the calling thread. This must be replaced with an async-safe
  inter-process coordination mechanism in Phase 6.
- `BeginTransaction()` on `SharedEngine` throws `NotSupportedException`. Explicit
  transactions in shared mode require Phase 6 redesign of the mutex lifetime.
- `DisposeAsync()` delegates to sync `Dispose` (Phase 6 deferred).

## `LiteDatabase` Pragma Properties

The synchronous property accessors (`UserVersion`, `Timeout`, `UtcDate`, `LimitSize`,
`CheckpointSize`, `Collation`) use `.GetAwaiter().GetResult()` to bridge the async pragma
engine call into a synchronous property. This is acceptable as a convenience bridge on the
client facade; however, callers that need to avoid sync-over-async should use the async
`Pragma(name, ct)` method directly.

