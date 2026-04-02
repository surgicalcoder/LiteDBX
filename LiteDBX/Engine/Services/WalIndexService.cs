using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

/// <summary>
/// Do all WAL index services based on LOG file - has only single instance per engine.
/// [Singleton - ThreadSafe]
///
/// Phase 3 changes:
/// <list type="bullet">
///   <item><see cref="ReaderWriterLockSlim"/> replaced with <see cref="AsyncReaderWriterLock"/>
///     so that write-lock acquisition does not block threads in async contexts.</item>
///   <item><see cref="Checkpoint"/> and <see cref="TryCheckpoint"/> are now <c>async</c>,
///     awaiting the exclusive lock and calling <see cref="DiskService.ReadFullAsync"/> /
///     <see cref="DiskService.WriteDataDisk"/>.</item>
///   <item><see cref="ConfirmTransaction"/> acquires the index write lock asynchronously.</item>
///   <item><see cref="GetPageIndex"/> keeps a sync bridge entry via
///     <see cref="AsyncReaderWriterLock.EnterReadSync"/> for the Phase 4 QueryExecutor path.</item>
///   <item><see cref="Clear"/> and the <c>ref</c>-based <see cref="RestoreIndex(ref HeaderPage)"/>
///     remain sync bridges for constructor-based startup; the explicit async-open lifecycle uses
///     <see cref="RestoreIndex(HeaderPage, CancellationToken)"/>.</item>
/// </list>
/// </summary>
internal class WalIndexService
{
    private readonly HashSet<uint> _confirmTransactions = new();
    private readonly DiskService _disk;
    private readonly Dictionary<uint, List<KeyValuePair<int, long>>> _index = new();

    // Phase 3: replaced ReaderWriterLockSlim with AsyncReaderWriterLock
    private readonly AsyncReaderWriterLock _indexLock = new();
    private readonly LockService _locker;

    /// <summary>Cached lock timeout, captured from <see cref="LockService.Timeout"/> at construction.</summary>
    private readonly TimeSpan _lockTimeout;

    private int _currentReadVersion;
    private int _lastTransactionID;

    public WalIndexService(DiskService disk, LockService locker)
    {
        _disk = disk;
        _locker = locker;
        _lockTimeout = locker.Timeout;
    }

    // -- CurrentReadVersion ----------------------------------------------------

    /// <summary>
    /// Get current read version for all new transactions.
    /// Phase 3 bridge: uses <see cref="AsyncReaderWriterLock.EnterReadSync"/> to avoid
    /// replacing the entire property access pattern in Phase 3.
    /// </summary>
    public int CurrentReadVersion
    {
        get
        {
            _indexLock.EnterReadSync(_lockTimeout);
            try
            {
                return _currentReadVersion;
            }
            finally
            {
                _indexLock.ExitRead();
            }
        }
    }

    public int LastTransactionID => _lastTransactionID;

    // -- Clear (sync, called from startup and from async CheckpointInternalAsync) ---

    /// <summary>
    /// Clear WAL index links and cache memory. Must be called while the index write lock is held.
    /// </summary>
    public void ClearUnderLock()
    {
        _confirmTransactions.Clear();
        _index.Clear();
        _lastTransactionID = 0;
        _currentReadVersion = 0;
        _disk.Cache.Clear();
        _disk.SetLength(0, FileOrigin.Log);
    }

    /// <summary>
    /// Clear WAL index and cache. Acquires the write lock synchronously.
    /// Kept for the sync startup/recovery path.
    /// </summary>
    public void Clear()
    {
        _indexLock.EnterWriteAsync(_lockTimeout).GetAwaiter().GetResult();
        try
        {
            ClearUnderLock();
        }
        finally
        {
            _indexLock.ExitWrite();
        }
    }

    // -- NextTransactionID -----------------------------------------------------

    public uint NextTransactionID() =>
        (uint)Interlocked.Increment(ref _lastTransactionID);

    // -- GetPageIndex (sync bridge) --------------------------------------------

    /// <summary>
    /// Check if a Page/Version is in WAL-index memory.
    /// Phase 3 bridge: uses a sync read-lock entry. Phase 4 will convert callers to async.
    /// </summary>
    public long GetPageIndex(uint pageID, int version, out int walVersion)
    {
        if (version == 0)
        {
            walVersion = 0;
            return long.MaxValue;
        }

        _indexLock.EnterReadSync(_lockTimeout);
        try
        {
            if (_index.TryGetValue(pageID, out var list))
            {
                var idx = list.Count;
                var position = long.MaxValue;
                walVersion = version;

                while (idx > 0)
                {
                    idx--;
                    var v = list[idx];
                    if (v.Key <= version)
                    {
                        walVersion = v.Key;
                        position = v.Value;
                        break;
                    }
                }

                return position;
            }

            walVersion = int.MaxValue;
            return long.MaxValue;
        }
        finally
        {
            _indexLock.ExitRead();
        }
    }

    // -- ConfirmTransaction (async) --------------------------------------------

    /// <summary>
    /// Add transactionID to the confirmed list and update the WAL index with page positions.
    /// Acquires the index write lock asynchronously.
    /// </summary>
    public async ValueTask ConfirmTransactionAsync(uint transactionID,
        ICollection<PagePosition> pagePositions, CancellationToken cancellationToken = default)
    {
        await _indexLock.EnterWriteAsync(_lockTimeout, cancellationToken).ConfigureAwait(false);
        try
        {
            _currentReadVersion++;

            foreach (var pos in pagePositions)
            {
                if (!_index.TryGetValue(pos.PageID, out var slot))
                {
                    slot = new List<KeyValuePair<int, long>>();
                    _index.Add(pos.PageID, slot);
                }

                slot.Add(new KeyValuePair<int, long>(_currentReadVersion, pos.Position));
            }

            _confirmTransactions.Add(transactionID);
        }
        finally
        {
            _indexLock.ExitWrite();
        }
    }

    /// <summary>
    /// Phase 4 bridge: synchronous version of <see cref="ConfirmTransactionAsync"/>.
    /// Used by the Recovery/Upgrade sync path.
    /// </summary>
    public void ConfirmTransaction(uint transactionID, ICollection<PagePosition> pagePositions)
    {
        _indexLock.EnterWriteAsync(_lockTimeout).GetAwaiter().GetResult();
        try
        {
            _currentReadVersion++;

            foreach (var pos in pagePositions)
            {
                if (!_index.TryGetValue(pos.PageID, out var slot))
                {
                    slot = new List<KeyValuePair<int, long>>();
                    _index.Add(pos.PageID, slot);
                }

                slot.Add(new KeyValuePair<int, long>(_currentReadVersion, pos.Position));
            }

            _confirmTransactions.Add(transactionID);
        }
        finally
        {
            _indexLock.ExitWrite();
        }
    }

    // -- RestoreIndex (sync, startup bridge) ----------------------------------

    /// <summary>
    /// Load all confirmed transactions from the log file during explicit async engine startup.
    /// Uses <see cref="DiskService.ReadFullAsync"/> and async index updates.
    /// </summary>
    public async ValueTask<HeaderPage> RestoreIndex(HeaderPage header, CancellationToken cancellationToken = default)
    {
        var positions = new Dictionary<long, List<PagePosition>>();
        var current = 0L;

        await foreach (var buffer in _disk.ReadFullAsync(FileOrigin.Log, cancellationToken).ConfigureAwait(false))
        {
            if (buffer.IsBlank())
            {
                current += PAGE_SIZE;
                continue;
            }

            var pageID = buffer.ReadUInt32(BasePage.P_PAGE_ID);
            var isConfirmed = buffer.ReadBool(BasePage.P_IS_CONFIRMED);
            var transactionID = buffer.ReadUInt32(BasePage.P_TRANSACTION_ID);

            var position = new PagePosition(pageID, current);

            if (positions.TryGetValue(transactionID, out var list))
                list.Add(position);
            else
                positions[transactionID] = new List<PagePosition> { position };

            if (isConfirmed)
            {
                await ConfirmTransactionAsync(transactionID, positions[transactionID], cancellationToken).ConfigureAwait(false);

                var pageType = (PageType)buffer.ReadByte(BasePage.P_PAGE_TYPE);

                if (pageType == PageType.Header)
                {
                    var headerBuffer = header.Buffer;
                    Buffer.BlockCopy(buffer.Array, buffer.Offset, headerBuffer.Array, headerBuffer.Offset, PAGE_SIZE);
                    header = new HeaderPage(headerBuffer);
                    header.TransactionID = uint.MaxValue;
                    header.IsConfirmed = false;
                }
            }

            _lastTransactionID = (int)transactionID;
            current += PAGE_SIZE;
        }

        return header;
    }

    /// <summary>
    /// Load all confirmed transactions from the log file (called during engine startup).
    /// Uses the sync <see cref="DiskService.ReadFull"/> path only for the legacy constructor-based
    /// startup bridge. Prefer <see cref="RestoreIndex(HeaderPage, CancellationToken)"/>.
    /// </summary>
    public void RestoreIndex(ref HeaderPage header)
    {
        var positions = new Dictionary<long, List<PagePosition>>();
        var current = 0L;

        foreach (var buffer in _disk.ReadFull(FileOrigin.Log))
        {
            if (buffer.IsBlank())
            {
                current += PAGE_SIZE;
                continue;
            }

            var pageID = buffer.ReadUInt32(BasePage.P_PAGE_ID);
            var isConfirmed = buffer.ReadBool(BasePage.P_IS_CONFIRMED);
            var transactionID = buffer.ReadUInt32(BasePage.P_TRANSACTION_ID);

            var position = new PagePosition(pageID, current);

            if (positions.TryGetValue(transactionID, out var list))
                list.Add(position);
            else
                positions[transactionID] = new List<PagePosition> { position };

            if (isConfirmed)
            {
                ConfirmTransaction(transactionID, positions[transactionID]);

                var pageType = (PageType)buffer.ReadByte(BasePage.P_PAGE_TYPE);

                if (pageType == PageType.Header)
                {
                    var headerBuffer = header.Buffer;
                    Buffer.BlockCopy(buffer.Array, buffer.Offset, headerBuffer.Array, headerBuffer.Offset, PAGE_SIZE);
                    header = new HeaderPage(headerBuffer);
                    header.TransactionID = uint.MaxValue;
                    header.IsConfirmed = false;
                }
            }

            _lastTransactionID = (int)transactionID;
            current += PAGE_SIZE;
        }
    }

    // -- Checkpoint (async) ----------------------------------------------------

    /// <summary>
    /// Checkpoint: copy all committed log pages into the data file.
    /// Returns the number of transactions committed to the data file.
    /// Acquires the exclusive lock asynchronously and delegates I/O to the async disk paths.
    /// </summary>
    public async ValueTask<int> Checkpoint(CancellationToken cancellationToken = default)
    {
        if (_disk.GetFileLength(FileOrigin.Log) == 0 || _confirmTransactions.Count == 0)
            return 0;

        var mustExit = await _locker.EnterExclusiveAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await CheckpointInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (mustExit)
                _locker.ExitExclusive();
        }
    }

    /// <summary>
    /// Run checkpoint only if there are no open transactions (non-blocking exclusive attempt).
    /// </summary>
    public async ValueTask<int> TryCheckpoint(CancellationToken cancellationToken = default)
    {
        if (_disk.GetFileLength(FileOrigin.Log) == 0 || _confirmTransactions.Count == 0)
            return 0;

        if (!_locker.TryEnterExclusive(out var mustExit))
            return 0;

        try
        {
            return await CheckpointInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (mustExit)
                _locker.ExitExclusive();
        }
    }

    private async ValueTask<int> CheckpointInternalAsync(CancellationToken cancellationToken)
    {
        LOG("checkpoint", "WAL");

        var counter = 0;

        // Collect confirmed pages from log file asynchronously.
        var pagesToWrite = new List<PageBuffer>();

        await foreach (var buffer in _disk.ReadFullAsync(FileOrigin.Log, cancellationToken)
                                          .ConfigureAwait(false))
        {
            if (buffer.IsBlank()) continue;

            var transactionID = buffer.ReadUInt32(BasePage.P_TRANSACTION_ID);

            if (_confirmTransactions.Contains(transactionID))
            {
                var pageID = buffer.ReadUInt32(BasePage.P_PAGE_ID);

                buffer.Write(uint.MaxValue, BasePage.P_TRANSACTION_ID);
                buffer.Write(false, BasePage.P_IS_CONFIRMED);
                buffer.Position = BasePage.GetPagePosition(pageID);

                // ReadFullAsync reuses its internal byte[] between iterations.
                // Clone bytes per page so queued checkpoint writes keep stable content.
                var pageCopy = new PageBuffer(new byte[PAGE_SIZE], 0, 0)
                {
                    Position = buffer.Position,
                    Origin = FileOrigin.Data,
                    ShareCounter = 0
                };

                Buffer.BlockCopy(buffer.Array, buffer.Offset, pageCopy.Array, pageCopy.Offset, PAGE_SIZE);

                pagesToWrite.Add(pageCopy);
                counter++;
            }
        }

        // Write collected pages to the data file asynchronously.
        await _disk.WriteDataDisk(pagesToWrite, cancellationToken).ConfigureAwait(false);

        // Reset WAL index and cache.
        await _indexLock.EnterWriteAsync(_lockTimeout, cancellationToken).ConfigureAwait(false);
        try
        {
            ClearUnderLock();
        }
        finally
        {
            _indexLock.ExitWrite();
        }

        return counter;
    }
}