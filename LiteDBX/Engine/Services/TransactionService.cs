using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

/// <summary>
/// Manages state and page access for a single transaction.
///
/// Phase 2 redesign:
/// <list type="bullet">
///   <item><c>ThreadID</c> property removed — transaction ownership is no longer thread-affine.</item>
///   <item><see cref="CreateSnapshotAsync"/> added as the Phase 2 async path for snapshot creation.</item>
///   <item><see cref="CreateSnapshot"/> retained as Phase 3 bridge for legacy sync callers (QueryExecutor).</item>
/// </list>
/// </summary>
internal class TransactionService : IDisposable
{
    private readonly DiskService _disk;

    // instances from Engine
    private readonly HeaderPage _header;
    private readonly LockService _locker;
    private readonly TransactionMonitor _monitor;
    private readonly DiskReader _reader;

    // transaction controls
    private readonly Dictionary<string, Snapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);

    // transaction info
    private readonly WalIndexService _walIndex;

    public TransactionService(HeaderPage header, LockService locker, DiskService disk, WalIndexService walIndex, int maxTransactionSize, TransactionMonitor monitor, bool queryOnly)
    {
        // retain instances
        _header = header;
        _locker = locker;
        _disk = disk;
        _walIndex = walIndex;
        _monitor = monitor;

        QueryOnly = queryOnly;
        MaxTransactionSize = maxTransactionSize;

        // create new transactionID
        TransactionID = walIndex.NextTransactionID();
        StartTime = DateTime.UtcNow;
        _reader = _disk.GetReader();
    }

    // expose (as read only)
    // ThreadID removed in Phase 2 — transaction ownership is no longer thread-affine.

    public uint TransactionID { get; }

    public TransactionState State { get; private set; } = TransactionState.Active;

    public LockMode Mode { get; private set; } = LockMode.Read;

    public TransactionPages Pages { get; } = new();

    public DateTime StartTime { get; }

    public IEnumerable<Snapshot> Snapshots => _snapshots.Values;
    public bool QueryOnly { get; }

    // get/set
    public int MaxTransactionSize { get; set; }

    /// <summary>
    /// Get/Set how many open cursor this transaction are running
    /// </summary>
    public List<CursorInfo> OpenCursors { get; } = new();

    /// <summary>
    /// Get/Set if this transaction was opened by BeginTrans() method (not by AutoTransaction/Cursor)
    /// </summary>
    public bool ExplicitTransaction { get; set; } = false;

    /// <summary>
    /// Create (or get from transaction-cache) snapshot and return.
    /// Phase 3 bridge: uses synchronous lock acquisition via <see cref="LockService.EnterLockSync"/>.
    /// Use <see cref="CreateSnapshotAsync"/> for Phase 2+ async callers.
    /// </summary>
    public Snapshot CreateSnapshot(LockMode mode, string collection, bool addIfNotExists)
    {
        ENSURE(State == TransactionState.Active, "transaction must be active to create new snapshot");

        Snapshot create()
        {
            // lockAlreadyAcquired=false: constructor will call EnterLockSync (Phase 3 bridge)
            return new Snapshot(mode, collection, _header, TransactionID, Pages, _locker, _walIndex, _reader, _disk, addIfNotExists, lockAlreadyAcquired: false);
        }

        if (_snapshots.TryGetValue(collection, out var snapshot))
        {
            if ((mode == LockMode.Write && snapshot.Mode == LockMode.Read) || (addIfNotExists && snapshot.CollectionPage == null))
            {
                snapshot.Dispose();
                _snapshots.Remove(collection);
                _snapshots[collection] = snapshot = create();
            }
        }
        else
        {
            _snapshots[collection] = snapshot = create();
        }

        if (mode == LockMode.Write)
        {
            Mode = LockMode.Write;
        }

        return snapshot;
    }

    /// <summary>
    /// Async version of <see cref="CreateSnapshot"/>. Acquires the collection write lock asynchronously.
    /// This is the Phase 2 primary path; use this from all callers within <c>AutoTransactionAsync</c>.
    /// </summary>
    public async ValueTask<Snapshot> CreateSnapshotAsync(LockMode mode, string collection, bool addIfNotExists, CancellationToken ct = default)
    {
        ENSURE(State == TransactionState.Active, "transaction must be active to create new snapshot");

        async ValueTask<Snapshot> createAsync()
        {
            return await Snapshot.CreateAsync(mode, collection, _header, TransactionID, Pages, _locker, _walIndex, _reader, _disk, addIfNotExists, ct).ConfigureAwait(false);
        }

        if (_snapshots.TryGetValue(collection, out var snapshot))
        {
            if ((mode == LockMode.Write && snapshot.Mode == LockMode.Read) || (addIfNotExists && snapshot.CollectionPage == null))
            {
                snapshot.Dispose();
                _snapshots.Remove(collection);
                _snapshots[collection] = snapshot = await createAsync().ConfigureAwait(false);
            }
        }
        else
        {
            _snapshots[collection] = snapshot = await createAsync().ConfigureAwait(false);
        }

        if (mode == LockMode.Write)
        {
            Mode = LockMode.Write;
        }

        return snapshot;
    }

    /// <summary>Public implementation of Dispose pattern.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    // Finalizer removed in Phase 2: GC-based cleanup that called _monitor.ReleaseTransaction()
    // was inherently thread-affine and unsafe in an async context.
    // Users must use explicit disposal (await using LiteTransaction / explicit Release) to ensure
    // the transaction gate is correctly released. Leaked transactions will not be auto-cleaned by GC.

    /// <summary>
    /// If current transaction contains too much pages, now is safe to remove clean pages from memory and flush to wal disk
    /// dirty pages
    /// </summary>
    public void Safepoint()
    {
        if (State != TransactionState.Active)
        {
            throw new LiteException(0, "This transaction are invalid state");
        }

        if (_monitor.CheckSafepoint(this))
        {
            LOG($"safepoint flushing transaction pages: {Pages.TransactionSize}", "TRANSACTION");

            // if any snapshot are writable, persist pages
            if (Mode == LockMode.Write)
            {
                PersistDirtyPages(false);
            }

            // clear local pages in all snapshots (read/write snapshosts)
            foreach (var snapshot in _snapshots.Values)
            {
                snapshot.Clear();
            }

            // there is no local pages in cache and all dirty pages are in log file
            Pages.TransactionSize = 0;
        }
    }

    /// <summary>
    /// Phase 4 bridge: synchronous version of <see cref="PersistDirtyPagesAsync"/> for
    /// <see cref="Safepoint"/> and the sync <see cref="Commit"/> bridge.
    /// Uses <see cref="DiskService.WriteLogDiskSync"/>.
    /// </summary>
    private int PersistDirtyPages(bool commit)
    {
        IEnumerable<PageBuffer> source()
        {
            var pages = _snapshots.Values
                                  .Where(x => x.Mode == LockMode.Write)
                                  .SelectMany(x => x.GetWritablePages(true, commit));

            var markLastAsConfirmed = commit && !Pages.HeaderChanged;

            foreach (var page in pages.IsLast())
            {
                page.Item.TransactionID = TransactionID;

                if (page.IsLast)
                    page.Item.IsConfirmed = markLastAsConfirmed;

                if (Pages.LastDeletedPageID == page.Item.PageID && commit)
                {
                    ENSURE(Pages.HeaderChanged, "must header be in lock");
                    ENSURE(page.Item.PageType == PageType.Empty, "must be marked as deleted page");
                    page.Item.NextPageID = _header.FreeEmptyPageList;
                    _header.FreeEmptyPageList = Pages.FirstDeletedPageID;
                }

                var buffer = page.Item.UpdateBuffer();
                yield return buffer;

                Pages.DirtyPages[page.Item.PageID] = new PagePosition(page.Item.PageID, buffer.Position);
            }

            if (commit && Pages.HeaderChanged)
            {
                _header.TransactionID = TransactionID;
                _header.IsConfirmed = true;
                Pages.OnCommit(_header);

                var buffer = _header.UpdateBuffer();
                var clone = _disk.NewPage();
                Buffer.BlockCopy(buffer.Array, buffer.Offset, clone.Array, clone.Offset, clone.Count);
                yield return clone;
            }
        }

        var count = _disk.WriteLogDiskSync(source());

        _disk.DiscardCleanPages(_snapshots.Values
                                          .Where(x => x.Mode == LockMode.Write)
                                          .SelectMany(x => x.GetWritablePages(false, commit))
                                          .Select(x => x.Buffer));

        return count;
    }

    /// <summary>
    /// Persist all dirty in-memory pages (in all snapshots) and confirm them in the WAL.
    /// Returns the number of pages written.
    ///
    /// Phase 3: uses <see cref="DiskService.WriteLogDisk"/> (async) and
    /// <see cref="WalIndexService.ConfirmTransactionAsync"/>.
    /// The caller must hold <see cref="TransactionMonitor.HeaderCommitGate"/> before calling this
    /// method to serialise concurrent commits that modify the header page.
    /// </summary>
    private async ValueTask<int> PersistDirtyPagesAsync(bool commit, CancellationToken ct)
    {

        IEnumerable<PageBuffer> source()
        {
            var pages = _snapshots.Values
                                  .Where(x => x.Mode == LockMode.Write)
                                  .SelectMany(x => x.GetWritablePages(true, commit));

            var markLastAsConfirmed = commit && !Pages.HeaderChanged;

            foreach (var page in pages.IsLast())
            {
                page.Item.TransactionID = TransactionID;

                if (page.IsLast)
                    page.Item.IsConfirmed = markLastAsConfirmed;

                if (Pages.LastDeletedPageID == page.Item.PageID && commit)
                {
                    ENSURE(Pages.HeaderChanged, "must header be in lock");
                    ENSURE(page.Item.PageType == PageType.Empty, "must be marked as deleted page");
                    page.Item.NextPageID = _header.FreeEmptyPageList;
                    _header.FreeEmptyPageList = Pages.FirstDeletedPageID;
                }

                var buffer = page.Item.UpdateBuffer();
                yield return buffer;

                Pages.DirtyPages[page.Item.PageID] = new PagePosition(page.Item.PageID, buffer.Position);
            }

            if (commit && Pages.HeaderChanged)
            {
                _header.TransactionID = TransactionID;
                _header.IsConfirmed = true;
                Pages.OnCommit(_header);

                var buffer = _header.UpdateBuffer();
                var clone = _disk.NewPage();
                Buffer.BlockCopy(buffer.Array, buffer.Offset, clone.Array, clone.Offset, clone.Count);
                yield return clone;
            }
        }

        var count = await _disk.WriteLogDisk(source(), ct).ConfigureAwait(false);

        _disk.DiscardCleanPages(_snapshots.Values
                                          .Where(x => x.Mode == LockMode.Write)
                                          .SelectMany(x => x.GetWritablePages(false, commit))
                                          .Select(x => x.Buffer));

        return count;
    }

    /// <summary>
    /// Asynchronously commit the transaction: persist dirty pages, confirm in WAL, dispose snapshots.
    ///
    /// Phase 3: replaces <c>lock(_header)</c> with <see cref="TransactionMonitor.HeaderCommitGate"/>
    /// so the commit path never blocks a thread-pool thread.
    /// </summary>
    public async ValueTask CommitAsync(CancellationToken ct = default)
    {
        ENSURE(State == TransactionState.Active, "transaction must be active to commit (current state: {0})", State);

        LOG($"commit transaction ({Pages.TransactionSize} pages)", "TRANSACTION");

        if (Mode == LockMode.Write || Pages.HeaderChanged)
        {
            await _monitor.HeaderCommitGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var count = await PersistDirtyPagesAsync(true, ct).ConfigureAwait(false);

                if (count > 0)
                    await _walIndex.ConfirmTransactionAsync(TransactionID, Pages.DirtyPages.Values, ct)
                                   .ConfigureAwait(false);
            }
            finally
            {
                _monitor.HeaderCommitGate.Release();
            }
        }

        foreach (var snapshot in _snapshots.Values)
            snapshot.Dispose();

        State = TransactionState.Committed;
    }

    /// <summary>
    /// Phase 4 bridge: synchronous commit for code paths not yet converted.
    /// Prefer <see cref="CommitAsync"/> from all async callers.
    /// </summary>
    public void Commit()
    {
        ENSURE(State == TransactionState.Active, "transaction must be active to commit (current state: {0})", State);

        LOG($"commit transaction ({Pages.TransactionSize} pages)", "TRANSACTION");

        if (Mode == LockMode.Write || Pages.HeaderChanged)
        {
            _monitor.HeaderCommitGate.Wait();
            try
            {
                var count = PersistDirtyPages(true);
                if (count > 0)
                    _walIndex.ConfirmTransaction(TransactionID, Pages.DirtyPages.Values);
            }
            finally
            {
                _monitor.HeaderCommitGate.Release();
            }
        }

        foreach (var snapshot in _snapshots.Values)
            snapshot.Dispose();

        State = TransactionState.Committed;
    }

    private static IEnumerable<PageBuffer> GetStillWritableBuffers(Snapshot snapshot, bool dirty)
    {
        return snapshot
            .GetWritablePages(dirty, true)
            .Select(x => x.Buffer)
            .Where(x => x.ShareCounter == BUFFER_WRITABLE);
    }

    /// <summary>
    /// Asynchronously rollback the transaction: discard dirty pages, return new pages, dispose snapshots.
    ///
    /// Phase 3: <see cref="ReturnNewPagesAsync"/> uses the async WAL write path.
    /// </summary>
    public async ValueTask RollbackAsync(CancellationToken ct = default)
    {
        ENSURE(State == TransactionState.Active, "transaction must be active to rollback (current state: {0})", State);

        LOG($"rollback transaction ({Pages.TransactionSize} pages with {Pages.NewPages.Count} returns)", "TRANSACTION");

        if (Pages.NewPages.Count > 0)
            await ReturnNewPagesAsync(ct).ConfigureAwait(false);

        foreach (var snapshot in _snapshots.Values)
        {
            if (snapshot.Mode == LockMode.Write)
            {
                _disk.DiscardDirtyPages(GetStillWritableBuffers(snapshot, true));
                _disk.DiscardCleanPages(GetStillWritableBuffers(snapshot, false));
            }

            snapshot.Dispose();
        }

        State = TransactionState.Aborted;
    }

    /// <summary>
    /// Phase 4 bridge: synchronous rollback for paths not yet converted.
    /// Prefer <see cref="RollbackAsync"/> from all async callers.
    /// </summary>
    public void Rollback()
    {
        ENSURE(State == TransactionState.Active, "transaction must be active to rollback (current state: {0})", State);

        LOG($"rollback transaction ({Pages.TransactionSize} pages with {Pages.NewPages.Count} returns)", "TRANSACTION");

        if (Pages.NewPages.Count > 0)
            ReturnNewPages();

        foreach (var snapshot in _snapshots.Values)
        {
            if (snapshot.Mode == LockMode.Write)
            {
                _disk.DiscardDirtyPages(GetStillWritableBuffers(snapshot, true));
                _disk.DiscardCleanPages(GetStillWritableBuffers(snapshot, false));
            }

            snapshot.Dispose();
        }

        State = TransactionState.Aborted;
    }

    /// <summary>
    /// Async: return added pages on rollback by writing them as EmptyPage entries in the log.
    /// Phase 3 primary path.
    /// </summary>
    private async ValueTask ReturnNewPagesAsync(CancellationToken ct)
    {
        var transactionID = _walIndex.NextTransactionID();

        await _monitor.HeaderCommitGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var pagePositions = new Dictionary<uint, PagePosition>();

            IEnumerable<PageBuffer> source()
            {
                for (var i = 0; i < Pages.NewPages.Count; i++)
                {
                    var pageID = Pages.NewPages[i];
                    var next = i < Pages.NewPages.Count - 1 ? Pages.NewPages[i + 1] : _header.FreeEmptyPageList;

                    var buffer = _disk.NewPage();
                    var page = new BasePage(buffer, pageID, PageType.Empty)
                    {
                        NextPageID = next,
                        TransactionID = transactionID
                    };

                    yield return page.UpdateBuffer();
                    pagePositions[pageID] = new PagePosition(pageID, buffer.Position);
                }

                _header.TransactionID = transactionID;
                _header.FreeEmptyPageList = Pages.NewPages[0];
                _header.IsConfirmed = true;

                var buf = _header.UpdateBuffer();
                var clone = _disk.NewPage();
                Buffer.BlockCopy(buf.Array, buf.Offset, clone.Array, clone.Offset, clone.Count);
                yield return clone;
            }

            var safepoint = _header.Savepoint();
            try
            {
                await _disk.WriteLogDisk(source(), ct).ConfigureAwait(false);
            }
            catch
            {
                _header.Restore(safepoint);
                throw;
            }

            await _walIndex.ConfirmTransactionAsync(transactionID, pagePositions.Values, ct)
                           .ConfigureAwait(false);
        }
        finally
        {
            _monitor.HeaderCommitGate.Release();
        }
    }

    /// <summary>
    /// Phase 4 bridge: synchronous return-new-pages for paths not yet converted.
    /// </summary>
    private void ReturnNewPages()
    {
        var transactionID = _walIndex.NextTransactionID();

        _monitor.HeaderCommitGate.Wait();
        try
        {
            var pagePositions = new Dictionary<uint, PagePosition>();

            IEnumerable<PageBuffer> source()
            {
                for (var i = 0; i < Pages.NewPages.Count; i++)
                {
                    var pageID = Pages.NewPages[i];
                    var next = i < Pages.NewPages.Count - 1 ? Pages.NewPages[i + 1] : _header.FreeEmptyPageList;

                    var buffer = _disk.NewPage();
                    var page = new BasePage(buffer, pageID, PageType.Empty)
                    {
                        NextPageID = next,
                        TransactionID = transactionID
                    };

                    yield return page.UpdateBuffer();
                    pagePositions[pageID] = new PagePosition(pageID, buffer.Position);
                }

                _header.TransactionID = transactionID;
                _header.FreeEmptyPageList = Pages.NewPages[0];
                _header.IsConfirmed = true;

                var buf = _header.UpdateBuffer();
                var clone = _disk.NewPage();
                Buffer.BlockCopy(buf.Array, buf.Offset, clone.Array, clone.Offset, clone.Count);
                yield return clone;
            }

            var safepoint = _header.Savepoint();
            try
            {
                _disk.WriteLogDiskSync(source());
            }
            catch
            {
                _header.Restore(safepoint);
                throw;
            }

            _walIndex.ConfirmTransaction(transactionID, pagePositions.Values);
        }
        finally
        {
            _monitor.HeaderCommitGate.Release();
        }
    }

    // Protected implementation of Dispose pattern.
    protected virtual void Dispose(bool dispose)
    {
        if (State == TransactionState.Disposed)
        {
            return;
        }

        ENSURE(State != TransactionState.Disposed, "transaction must be active before call Done");

        // clean snapshots if there is no commit/rollback
        if (State == TransactionState.Active && _snapshots.Count > 0)
        {
            foreach (var snapshot in _snapshots.Values)
            {
                if (snapshot.Mode == LockMode.Write)
                {
                    _disk.DiscardDirtyPages(GetStillWritableBuffers(snapshot, true));
                    _disk.DiscardCleanPages(GetStillWritableBuffers(snapshot, false));
                }

                // Always dispose snapshots so write snapshots release collection locks.
                snapshot.Dispose();
            }
        }

        _reader.Dispose();

        State = TransactionState.Disposed;

        // Phase 2: the finalizer no longer calls ReleaseTransaction.
        // ReleaseTransaction (which exits the transaction gate) must be called explicitly
        // via LiteTransaction.DisposeAsync or TransactionMonitor.ReleaseTransaction.
    }
}

