using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteDbX.Engine;

namespace LiteDbX;

/// <summary>
/// Supported shared-access mode for <b>physical file-backed</b> databases that need
/// cross-process write coordination.
///
/// <para>
/// <see cref="LockFileEngine"/> coordinates write operations through an exclusive lock file and
/// keeps the underlying <see cref="LiteEngine"/> open only for the duration of each outermost
/// call. Reads also use short-lived leases, so handles are not kept open between operations.
/// Nested operations in the same async flow reuse the same engine lease.
/// </para>
///
/// <para>
/// This mode is limited to physical filename-based databases. It does not support custom streams,
/// <c>:memory:</c>, or <c>:temp:</c>.
/// </para>
///
/// <para>
/// Explicit transaction scopes are not supported. Use direct mode when you need
/// <see cref="ILiteTransaction"/> scope.
/// </para>
/// </summary>
public class LockFileEngine : ILiteEngine, IDisposable
{
    private const int LockRetryDelayMilliseconds = 25;

    private sealed class SharedSession
    {
        public int RefCount { get; set; }

        public bool HasWriteAccess { get; set; }
    }

    private sealed class LeaseContext
    {
        public LeaseContext(LockFileEngine owner, SharedSession session, LeaseContext previous)
        {
            Owner = owner;
            Session = session;
            Previous = previous;
        }

        public LockFileEngine Owner { get; }
        public SharedSession Session { get; }
        public LeaseContext Previous { get; }
    }

    private sealed class Lease : IDisposable, IAsyncDisposable
    {
        private readonly LockFileEngine _owner;
        private readonly LeaseContext _context;
        private readonly bool _ownsAmbientContext;
        private readonly SharedSession _session;
        private bool _disposed;

        public Lease(
            LockFileEngine owner,
            SharedSession session,
            LeaseContext context,
            bool ownsAmbientContext,
            LiteEngine engine)
        {
            _owner = owner;
            _session = session;
            _context = context;
            _ownsAmbientContext = ownsAmbientContext;
            Engine = engine;
        }

        public LiteEngine Engine { get; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner.ReleaseLease(_session, _context, _ownsAmbientContext);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }
    }

    private static readonly AsyncLocal<LeaseContext> _currentLease = new AsyncLocal<LeaseContext>();
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    private readonly object _syncRoot = new object();
    private readonly EngineSettings _settings;
    private SharedSession _activeSession;
    private LiteEngine _engine;
    private FileStream _lockStream;
    private bool _disposeRequested;
    private bool _disposed;

    public LockFileEngine(EngineSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        if (!SupportsLockFile(settings))
        {
            throw new NotSupportedException(
                "ConnectionType.LockFile is supported only for physical file-backed databases and does not support custom streams, ':memory:', or ':temp:'.");
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~LockFileEngine() { Dispose(false); }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;

        LiteEngine engineToDispose = null;
        FileStream lockStreamToDispose = null;
        var disposeGate = false;

        lock (_syncRoot)
        {
            if (_disposed || _disposeRequested) return;

            _disposeRequested = true;

            if (_activeSession == null)
            {
                _disposed = true;
                engineToDispose = _engine;
                _engine = null;
                lockStreamToDispose = _lockStream;
                _lockStream = null;
                disposeGate = true;
            }
        }

        try
        {
            engineToDispose?.Dispose();
        }
        finally
        {
            lockStreamToDispose?.Dispose();

            if (disposeGate)
            {
                _gate.Dispose();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        LiteEngine engineToDispose = null;
        FileStream lockStreamToDispose = null;
        var disposeGate = false;

        lock (_syncRoot)
        {
            if (_disposed || _disposeRequested) return;

            _disposeRequested = true;

            if (_activeSession == null)
            {
                _disposed = true;
                engineToDispose = _engine;
                _engine = null;
                lockStreamToDispose = _lockStream;
                _lockStream = null;
                disposeGate = true;
            }
        }

        try
        {
            if (engineToDispose != null)
            {
                await engineToDispose.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            lockStreamToDispose?.Dispose();

            if (disposeGate)
            {
                _gate.Dispose();
            }
        }

        GC.SuppressFinalize(this);
    }

    private static bool SupportsLockFile(EngineSettings settings)
    {
        return settings.DataStream == null &&
               settings.LogStream == null &&
               settings.TempStream == null &&
               !string.IsNullOrWhiteSpace(settings.Filename) &&
               settings.Filename != ":memory:" &&
               settings.Filename != ":temp:";
    }

    private async ValueTask<Lease> AcquireLeaseAsync(bool write, CancellationToken cancellationToken)
    {
        var requiresWriteAccess = RequiresWriteAccess(write);
        var ambient = _currentLease.Value;

        if (ambient != null && ReferenceEquals(ambient.Owner, this))
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(LockFileEngine));
                }

                if (!ReferenceEquals(_activeSession, ambient.Session) || _engine == null)
                {
                    throw new InvalidOperationException("LockFileEngine ambient lease is no longer active.");
                }

                if (requiresWriteAccess && !ambient.Session.HasWriteAccess)
                {
                    throw new InvalidOperationException("Cannot escalate a read-only LockFileEngine lease to write access.");
                }

                ambient.Session.RefCount++;

                return new Lease(this, ambient.Session, ambient, ownsAmbientContext: false, _engine);
            }
        }

        lock (_syncRoot)
        {
            if (_disposed || _disposeRequested)
            {
                throw new ObjectDisposedException(nameof(LockFileEngine));
            }
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        FileStream lockStream = null;

        try
        {
            if (requiresWriteAccess)
            {
                lockStream = await AcquireWriteLockAsync(cancellationToken).ConfigureAwait(false);
            }

            var engine = await LiteEngine.Open(CreateOperationSettings(requiresWriteAccess), cancellationToken).ConfigureAwait(false);

            lock (_syncRoot)
            {
                if (_disposed || _disposeRequested)
                {
                    engine.Dispose();
                    lockStream?.Dispose();
                    throw new ObjectDisposedException(nameof(LockFileEngine));
                }

                var session = new SharedSession { RefCount = 1, HasWriteAccess = requiresWriteAccess };
                var context = new LeaseContext(this, session, ambient);

                _engine = engine;
                _lockStream = lockStream;
                _activeSession = session;
                _currentLease.Value = context;

                return new Lease(this, session, context, ownsAmbientContext: true, engine);
            }
        }
        catch
        {
            lockStream?.Dispose();
            _gate.Release();
            throw;
        }
    }

    private bool RequiresWriteAccess(bool requestedWrite)
    {
        if (requestedWrite)
        {
            return true;
        }

        if (_settings.ReadOnly)
        {
            return false;
        }

        return !File.Exists(_settings.Filename);
    }

    private EngineSettings CreateOperationSettings(bool write)
    {
        return new EngineSettings
        {
            DataStream = _settings.DataStream,
            LogStream = _settings.LogStream,
            TempStream = _settings.TempStream,
            Filename = _settings.Filename,
            Password = _settings.Password,
            InitialSize = _settings.InitialSize,
            Collation = _settings.Collation,
            ReadOnly = write ? _settings.ReadOnly : ShouldOpenReadOnlyLease(),
            AutoRebuild = _settings.AutoRebuild,
            Upgrade = _settings.Upgrade,
            ReadTransform = _settings.ReadTransform
        };
    }

    private bool ShouldOpenReadOnlyLease()
    {
        if (_settings.ReadOnly)
        {
            return true;
        }

        return File.Exists(_settings.Filename);
    }

    private async ValueTask<FileStream> AcquireWriteLockAsync(CancellationToken cancellationToken)
    {
        var lockFilename = FileHelper.GetLockFile(_settings.Filename);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return new FileStream(
                    lockFilename,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            }
            catch (IOException ex) when (ex.IsLocked())
            {
                await Task.Delay(LockRetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void ReleaseLease(SharedSession session, LeaseContext context, bool ownsAmbientContext)
    {
        LiteEngine engineToDispose = null;
        FileStream lockStreamToDispose = null;
        var releaseGate = false;
        var disposeGate = false;

        lock (_syncRoot)
        {
            if (ownsAmbientContext && ReferenceEquals(_currentLease.Value, context))
            {
                _currentLease.Value = context.Previous;
            }

            if (session.RefCount == 0)
            {
                return;
            }

            session.RefCount--;

            if (session.RefCount == 0)
            {
                if (ReferenceEquals(_activeSession, session))
                {
                    _activeSession = null;
                }

                engineToDispose = _engine;
                _engine = null;

                lockStreamToDispose = _lockStream;
                _lockStream = null;

                if (_disposeRequested)
                {
                    _disposed = true;
                    disposeGate = true;
                }

                releaseGate = true;
            }
        }

        try
        {
            engineToDispose?.Dispose();
        }
        finally
        {
            lockStreamToDispose?.Dispose();

            if (releaseGate)
            {
                _gate.Release();

                if (disposeGate)
                {
                    _gate.Dispose();
                }
            }
        }
    }

    private async ValueTask<T> ExecuteWithLeaseAsync<T>(
        Func<LiteEngine, ValueTask<T>> operation,
        bool write,
        CancellationToken cancellationToken = default)
    {
        await using var lease = await AcquireLeaseAsync(write, cancellationToken).ConfigureAwait(false);

        return await operation(lease.Engine).ConfigureAwait(false);
    }

    public ValueTask<ILiteTransaction> BeginTransaction(CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "ConnectionType.LockFile supports physical-file cross-process coordination only. " +
            "Explicit transactions are not supported; use ConnectionType.Direct for ILiteTransaction scope.");

    public IAsyncEnumerable<BsonDocument> Query(
        string collection,
        Query query,
        CancellationToken cancellationToken = default)
    {
        return QueryStream(collection, query, cancellationToken);
    }

    public IAsyncEnumerable<BsonDocument> Query(
        string collection,
        Query query,
        ILiteTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        if (transaction != null)
        {
            throw new NotSupportedException(
                "ConnectionType.LockFile does not support explicit transaction-bound queries. " +
                "Use ConnectionType.Direct for ILiteTransaction scope.");
        }

        return Query(collection, query, cancellationToken);
    }

    private async IAsyncEnumerable<BsonDocument> QueryStream(
        string collection,
        Query query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        List<BsonDocument> documents;

        await using (var lease = await AcquireLeaseAsync(write: false, cancellationToken).ConfigureAwait(false))
        {
            documents = new List<BsonDocument>();

            await foreach (var doc in lease.Engine.Query(collection, query, cancellationToken).ConfigureAwait(false))
            {
                documents.Add(doc);
            }
        }

        foreach (var doc in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return doc;
        }
    }

    public ValueTask<int> Insert(string collection, IEnumerable<BsonDocument> docs, BsonAutoId autoId, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.Insert(collection, docs, autoId, cancellationToken), write: true, cancellationToken);

    public ValueTask<int> Insert(string collection, IEnumerable<BsonDocument> docs, BsonAutoId autoId, ILiteTransaction transaction, CancellationToken cancellationToken = default)
    {
        if (transaction != null)
        {
            throw new NotSupportedException(
                "ConnectionType.LockFile does not support explicit transaction-bound inserts. " +
                "Use ConnectionType.Direct for ILiteTransaction scope.");
        }

        return Insert(collection, docs, autoId, cancellationToken);
    }

    public ValueTask<int> Update(string collection, IEnumerable<BsonDocument> docs, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.Update(collection, docs, cancellationToken), write: true, cancellationToken);

    public ValueTask<int> UpdateMany(string collection, BsonExpression extend, BsonExpression predicate, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.UpdateMany(collection, extend, predicate, cancellationToken), write: true, cancellationToken);

    public ValueTask<int> Upsert(string collection, IEnumerable<BsonDocument> docs, BsonAutoId autoId, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.Upsert(collection, docs, autoId, cancellationToken), write: true, cancellationToken);

    public ValueTask<int> Delete(string collection, IEnumerable<BsonValue> ids, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.Delete(collection, ids, cancellationToken), write: true, cancellationToken);

    public ValueTask<int> DeleteMany(string collection, BsonExpression predicate, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.DeleteMany(collection, predicate, cancellationToken), write: true, cancellationToken);

    public ValueTask<bool> DropCollection(string name, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.DropCollection(name, cancellationToken), write: true, cancellationToken);

    public ValueTask<bool> RenameCollection(string name, string newName, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.RenameCollection(name, newName, cancellationToken), write: true, cancellationToken);

    public ValueTask<bool> EnsureIndex(string collection, string name, BsonExpression expression, bool unique, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.EnsureIndex(collection, name, expression, unique, cancellationToken), write: true, cancellationToken);

    public ValueTask<bool> DropIndex(string collection, string name, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.DropIndex(collection, name, cancellationToken), write: true, cancellationToken);

    public ValueTask<int> Checkpoint(CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.Checkpoint(cancellationToken), write: true, cancellationToken);

    public ValueTask<long> Rebuild(RebuildOptions options, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.Rebuild(options, cancellationToken), write: true, cancellationToken);

    public ValueTask<BsonValue> Pragma(string name, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.Pragma(name, cancellationToken), write: false, cancellationToken);

    public ValueTask<bool> Pragma(string name, BsonValue value, CancellationToken cancellationToken = default)
        => ExecuteWithLeaseAsync(engine => engine.Pragma(name, value, cancellationToken), write: true, cancellationToken);
}

