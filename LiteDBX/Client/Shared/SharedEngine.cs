using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteDbX.Engine;
#if NETFRAMEWORK
using System.Security.AccessControl;
using System.Security.Principal;
#endif

namespace LiteDbX;

/// <summary>
/// Shared-mode engine wrapper that serialises multi-process access via a named OS mutex.
///
/// Phase 4 compile-correctness update: all methods now match the async <see cref="ILiteEngine"/>
/// contract. The internal mutex-based locking strategy (WaitOne / ReleaseMutex) remains
/// synchronous and is noted as a Phase 6 deferred item: the mutex acquisition must be replaced
/// with an async-safe inter-process coordination mechanism (e.g., async-compatible file locking
/// or a named semaphore with WaitAsync support) before blocking threads in async contexts is
/// acceptable.
///
/// Phase 6 deferred: full SharedEngine redesign for async-safe shared-mode operation.
/// </summary>
public class SharedEngine : ILiteEngine
{
    private readonly Mutex _mutex;
    private readonly EngineSettings _settings;
    private LiteEngine _engine;
    private bool _transactionRunning;

    public SharedEngine(EngineSettings settings)
    {
        _settings = settings;

        var name = Path.GetFullPath(settings.Filename).ToLower().Sha1();

        try
        {
#if NETFRAMEWORK
            var allowEveryoneRule = new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                MutexRights.FullControl, AccessControlType.Allow);
            var securitySettings = new MutexSecurity();
            securitySettings.AddAccessRule(allowEveryoneRule);
            _mutex = new Mutex(false, "Global\\" + name + ".Mutex", out _, securitySettings);
#else
            _mutex = new Mutex(false, "Global\\" + name + ".Mutex");
#endif
        }
        catch (NotSupportedException ex)
        {
            throw new PlatformNotSupportedException("Shared mode is not supported in platforms that do not implement named mutex.", ex);
        }
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
        if (disposing && _engine != null)
        {
            _engine.Dispose();
            _engine = null;
            _mutex.ReleaseMutex();
        }
    }

    /// <summary>Phase 6 deferred: DisposeAsync still delegates to sync Dispose.</summary>
    public ValueTask DisposeAsync()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
        return default;
    }

    // ── Mutex open/close helpers ───────────────────────────────────────────────

    /// <summary>
    /// Phase 6 bridge: WaitOne blocks the calling thread.
    /// Replace with an async-safe equivalent in Phase 6.
    /// </summary>
    private bool OpenDatabase()
    {
        try { _mutex.WaitOne(); } catch (AbandonedMutexException) { }

        if (!_transactionRunning && _engine == null)
        {
            try
            {
                _engine = new LiteEngine(_settings);
                return true;
            }
            catch
            {
                _mutex.ReleaseMutex();
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

        _mutex.ReleaseMutex();
    }

    private async ValueTask<T> QueryDatabaseAsync<T>(Func<ValueTask<T>> query)
    {
        var opened = OpenDatabase(); // Phase 6 bridge: blocking WaitOne
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
    /// Phase 6 deferred: SQL-level transaction scoping for SharedEngine requires
    /// redesign of the mutex lifetime to span multiple Execute() calls.
    /// </summary>
    public ValueTask<ILiteTransaction> BeginTransaction(CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "Explicit transactions in shared mode require Phase 6 redesign. " +
            "Use single-call operations or a dedicated LiteEngine instance.");

    // ── Query ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Phase 6 bridge: mutex is acquired on enumeration start and released on disposal of the stream.
    /// Blocking WaitOne is used until Phase 6 replaces this with async-safe coordination.
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
        var opened = OpenDatabase(); // Phase 6 bridge: blocking WaitOne

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
        => QueryDatabaseAsync(() => _engine.Insert(collection, docs, autoId, cancellationToken));

    public ValueTask<int> Update(string collection, IEnumerable<BsonDocument> docs, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.Update(collection, docs, cancellationToken));

    public ValueTask<int> UpdateMany(string collection, BsonExpression extend, BsonExpression predicate, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.UpdateMany(collection, extend, predicate, cancellationToken));

    public ValueTask<int> Upsert(string collection, IEnumerable<BsonDocument> docs, BsonAutoId autoId, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.Upsert(collection, docs, autoId, cancellationToken));

    public ValueTask<int> Delete(string collection, IEnumerable<BsonValue> ids, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.Delete(collection, ids, cancellationToken));

    public ValueTask<int> DeleteMany(string collection, BsonExpression predicate, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.DeleteMany(collection, predicate, cancellationToken));

    // ── Schema management ─────────────────────────────────────────────────────

    public ValueTask<bool> DropCollection(string name, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.DropCollection(name, cancellationToken));

    public ValueTask<bool> RenameCollection(string name, string newName, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.RenameCollection(name, newName, cancellationToken));

    public ValueTask<bool> EnsureIndex(string collection, string name, BsonExpression expression, bool unique, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.EnsureIndex(collection, name, expression, unique, cancellationToken));

    public ValueTask<bool> DropIndex(string collection, string name, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.DropIndex(collection, name, cancellationToken));

    // ── Maintenance ───────────────────────────────────────────────────────────

    public ValueTask<int> Checkpoint(CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.Checkpoint(cancellationToken));

    public ValueTask<long> Rebuild(RebuildOptions options, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.Rebuild(options, cancellationToken));

    // ── Pragmas ───────────────────────────────────────────────────────────────

    public ValueTask<BsonValue> Pragma(string name, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.Pragma(name, cancellationToken));

    public ValueTask<bool> Pragma(string name, BsonValue value, CancellationToken cancellationToken = default)
        => QueryDatabaseAsync(() => _engine.Pragma(name, value, cancellationToken));
}