using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteDbX.Engine;

namespace LiteDbX;

/// <summary>
/// Shared-mode engine wrapper that serialises concurrent access within a single process
/// via an async-safe <see cref="SemaphoreSlim"/>.
///
/// Phase 6 redesign decision:
///
///   The previous implementation used a named OS <see cref="Mutex"/> with a blocking
///   <c>WaitOne()</c> call, which violates the async-only architecture rule that no
///   blocking waits may exist in operational paths.
///
///   The <see cref="Mutex"/> has been replaced with a <c>SemaphoreSlim(1,1)</c>
///   whose <see cref="SemaphoreSlim.WaitAsync()"/> is used in all operational paths,
///   eliminating thread-blocking from the async runtime path.
///
///   Cross-process exclusive file coordination (the original named-mutex purpose)
///   is <b>explicitly deferred</b>. No hidden sync fallback is left in place.
///   A future phase may introduce async-safe cross-process coordination (e.g. a
///   polling file-lock strategy using async Task.Delay retries).
///
///   <see cref="BeginTransaction"/> remains unsupported: spanning an explicit
///   transaction across per-call open/close cycles requires a deeper lifecycle
///   redesign that is out of scope for Phase 6.
/// </summary>
public class SharedEngine : ILiteEngine
{
    // Phase 6: SemaphoreSlim replaces the blocking OS Mutex for in-process serialisation.
    // Cross-process coordination is explicitly deferred (see class doc).
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private readonly EngineSettings _settings;
    private LiteEngine _engine;
    private bool _transactionRunning;
    private bool _disposed;

    public SharedEngine(EngineSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~SharedEngine() { Dispose(false); }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (disposing)
        {
            _engine?.Dispose();
            _engine = null;
            _gate.Dispose();
        }
    }

    /// <summary>
    /// Phase 6: DisposeAsync properly awaits engine disposal and disposes the semaphore.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_engine != null)
        {
            await _engine.DisposeAsync().ConfigureAwait(false);
            _engine = null;
        }

        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    // ── Gate open/close helpers ────────────────────────────────────────────────

    /// <summary>
    /// Acquires the per-instance semaphore (async-safe, no thread blocking) and
    /// opens the engine if needed.
    /// Returns <c>true</c> when this call actually opened the engine (and is
    /// responsible for closing it via <see cref="CloseDatabase"/>).
    /// </summary>
    private async ValueTask<bool> OpenDatabaseAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (!_transactionRunning && _engine == null)
        {
            try
            {
                _engine = new LiteEngine(_settings);
                return true;
            }
            catch
            {
                _gate.Release();
                throw;
            }
        }

        return false;
    }

    private void CloseDatabase()
    {
        if (!_transactionRunning && _engine != null)
        {
            _engine.Dispose();
            _engine = null;
        }

        _gate.Release();
    }

    private async ValueTask<T> QueryDatabaseAsync<T>(
        Func<ValueTask<T>> query,
        CancellationToken cancellationToken = default)
    {
        var opened = await OpenDatabaseAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await query().ConfigureAwait(false);
        }
        finally
        {
            if (opened) CloseDatabase();
        }
    }

    // ── Transactions ─────────────────────────────────────────────────────────

    /// <summary>
    /// Explicit multi-call transaction scope requires holding the gate open across
    /// multiple <c>await</c> continuations and is not supported in the current
    /// per-call open/close shared-mode model.
    ///
    /// Phase 6 deferred: redesign SharedEngine lifecycle to support explicit
    /// transaction scopes before enabling this.
    /// </summary>
    public ValueTask<ILiteTransaction> BeginTransaction(CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "Explicit transactions are not supported in SharedEngine. " +
            "Use single-call operations, or use a dedicated LiteEngine instance for transaction scope. " +
            "(Phase 6 deferred: shared-mode transaction lifecycle redesign.)");

    // ── Query ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Acquires the gate (async, no thread blocking) before enumeration starts and releases
    /// it when the async stream is fully consumed or disposed.
    /// </summary>
    public IAsyncEnumerable<BsonDocument> Query(
        string collection,
        Query query,
        CancellationToken cancellationToken = default)
    {
        return QueryStream(collection, query, cancellationToken);
    }

    private async IAsyncEnumerable<BsonDocument> QueryStream(
        string collection,
        Query query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var opened = await OpenDatabaseAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await foreach (var doc in _engine.Query(collection, query, cancellationToken).ConfigureAwait(false))
            {
                yield return doc;
            }
        }
        finally
        {
            if (opened) CloseDatabase();
        }
    }

    // ── Write operations ──────────────────────────────────────────────────────

    public ValueTask<int> Insert(string collection, IEnumerable<BsonDocument> docs, BsonAutoId autoId, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.Insert(collection, docs, autoId, cancellationToken), cancellationToken);

    public ValueTask<int> Update(string collection, IEnumerable<BsonDocument> docs, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.Update(collection, docs, cancellationToken), cancellationToken);

    public ValueTask<int> UpdateMany(string collection, BsonExpression extend, BsonExpression predicate, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.UpdateMany(collection, extend, predicate, cancellationToken), cancellationToken);

    public ValueTask<int> Upsert(string collection, IEnumerable<BsonDocument> docs, BsonAutoId autoId, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.Upsert(collection, docs, autoId, cancellationToken), cancellationToken);

    public ValueTask<int> Delete(string collection, IEnumerable<BsonValue> ids, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.Delete(collection, ids, cancellationToken), cancellationToken);

    public ValueTask<int> DeleteMany(string collection, BsonExpression predicate, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.DeleteMany(collection, predicate, cancellationToken), cancellationToken);

    // ── Schema management ─────────────────────────────────────────────────────

    public ValueTask<bool> DropCollection(string name, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.DropCollection(name, cancellationToken), cancellationToken);

    public ValueTask<bool> RenameCollection(string name, string newName, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.RenameCollection(name, newName, cancellationToken), cancellationToken);

    public ValueTask<bool> EnsureIndex(string collection, string name, BsonExpression expression, bool unique, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.EnsureIndex(collection, name, expression, unique, cancellationToken), cancellationToken);

    public ValueTask<bool> DropIndex(string collection, string name, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.DropIndex(collection, name, cancellationToken), cancellationToken);

    // ── Maintenance ───────────────────────────────────────────────────────────

    public ValueTask<int> Checkpoint(CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.Checkpoint(cancellationToken), cancellationToken);

    public ValueTask<long> Rebuild(RebuildOptions options, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.Rebuild(options, cancellationToken), cancellationToken);

    // ── Pragmas ───────────────────────────────────────────────────────────────

    public ValueTask<BsonValue> Pragma(string name, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.Pragma(name, cancellationToken));

    public ValueTask<bool> Pragma(string name, BsonValue value, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.Pragma(name, value, cancellationToken));
}