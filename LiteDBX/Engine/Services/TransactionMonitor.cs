using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

/// <summary>
/// Tracks and manages all open transactions.
///
/// Phase 2 redesign:
/// <list type="bullet">
///   <item><c>ThreadLocal&lt;TransactionService&gt;</c> removed — thread identity is no longer used for ownership.</item>
///   <item>Explicit transactions are tracked via <see cref="LiteTransaction.CurrentAmbient"/> (AsyncLocal).</item>
///   <item><see cref="GetOrCreateTransactionAsync"/> is the primary entry point for the async operational path.</item>
///   <item><see cref="GetOrCreateTransactionSync"/> is a Phase 4 bridge for internal code not yet converted to async.</item>
/// </list>
/// [Singleton - ThreadSafe]
/// </summary>
internal class TransactionMonitor : IDisposable
{
    private readonly DiskService _disk;
    private readonly HeaderPage _header;
    private readonly LockService _locker;
    private readonly object _lock = new object();
    private readonly Dictionary<uint, TransactionService> _transactions = new();
    private readonly WalIndexService _walIndex;

    /// <summary>
    /// Serialises concurrent transaction commits that modify the header page.
    /// Replaces the former <c>lock(_header)</c> pattern in <see cref="TransactionService"/>
    /// so that commit operations can await the gate without blocking a thread.
    /// Phase 3 addition.
    /// </summary>
    internal readonly SemaphoreSlim HeaderCommitGate = new SemaphoreSlim(1, 1);

    public TransactionMonitor(HeaderPage header, LockService locker, DiskService disk, WalIndexService walIndex)
    {
        _header = header;
        _locker = locker;
        _disk = disk;
        _walIndex = walIndex;

        FreePages = MAX_TRANSACTION_SIZE;
        InitialSize = MAX_TRANSACTION_SIZE / MAX_OPEN_TRANSACTIONS;
    }

    public ICollection<TransactionService> Transactions => _transactions.Values;
    public int FreePages { get; private set; }
    public int InitialSize { get; }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var transaction in _transactions.Values)
                transaction.Dispose();
            _transactions.Clear();
        }

        HeaderCommitGate.Dispose();
    }

    // ── Async entry point (Phase 2 primary path) ──────────────────────────────

    /// <summary>
    /// Get or create a transaction for the current async execution context.
    ///
    /// If an explicit <see cref="LiteTransaction"/> is ambient (from <see cref="LiteEngine.BeginTransaction"/>),
    /// it is reused (<paramref name="isNew"/> = <c>false</c>).
    ///
    /// Otherwise a new auto-transaction is created, entered into the transaction gate, and tracked.
    /// The caller is responsible for committing and releasing it when <paramref name="isNew"/> is <c>true</c>.
    /// </summary>
    public async ValueTask<(TransactionService transaction, bool isNew)> GetOrCreateTransactionAsync(
        bool queryOnly,
        CancellationToken ct = default)
    {
        // Reuse explicit ambient transaction (set by BeginTransaction).
        if (!queryOnly && LiteTransaction.CurrentAmbient != null)
        {
            return (LiteTransaction.CurrentAmbient.Service, false);
        }

        // Create a new auto-transaction or query-only transaction.
        TransactionService transaction;

        lock (_lock)
        {
            if (_transactions.Count >= MAX_OPEN_TRANSACTIONS)
                throw new LiteException(0, "Maximum number of transactions reached");

            var initialSize = GetInitialSize();
            transaction = new TransactionService(_header, _locker, _disk, _walIndex, initialSize, this, queryOnly);
            _transactions[transaction.TransactionID] = transaction;
        }

        try
        {
            await _locker.EnterTransactionAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            lock (_lock)
            {
                FreePages += transaction.MaxTransactionSize;
                _transactions.Remove(transaction.TransactionID);
            }
            transaction.Dispose();
            throw;
        }

        return (transaction, true);
    }

    /// <summary>
    /// Create a new explicit transaction to back a <see cref="LiteTransaction"/> scope.
    /// Called exclusively from <see cref="LiteEngine.BeginTransaction"/>.
    /// </summary>
    public async ValueTask<TransactionService> CreateExplicitTransactionAsync(CancellationToken ct = default)
    {
        TransactionService transaction;

        lock (_lock)
        {
            if (_transactions.Count >= MAX_OPEN_TRANSACTIONS)
                throw new LiteException(0, "Maximum number of transactions reached");

            var initialSize = GetInitialSize();
            transaction = new TransactionService(_header, _locker, _disk, _walIndex, initialSize, this, false);
            transaction.ExplicitTransaction = true;
            _transactions[transaction.TransactionID] = transaction;
        }

        try
        {
            await _locker.EnterTransactionAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            lock (_lock)
            {
                FreePages += transaction.MaxTransactionSize;
                _transactions.Remove(transaction.TransactionID);
            }
            transaction.Dispose();
            throw;
        }

        return transaction;
    }

    // ── Sync bridge (Phase 4 target) ──────────────────────────────────────────

    /// <summary>
    /// Phase 4 bridge: synchronous version of <see cref="GetOrCreateTransactionAsync"/> for internal
    /// code paths not yet converted (e.g. <c>QueryExecutor</c>, <c>RebuildContent</c>).
    /// Uses blocking <see cref="LockService.EnterTransactionSync"/>.
    /// </summary>
    internal TransactionService GetOrCreateTransactionSync(bool queryOnly, out bool isNew)
    {
        // Reuse explicit ambient transaction.
        if (!queryOnly && LiteTransaction.CurrentAmbient != null)
        {
            isNew = false;
            return LiteTransaction.CurrentAmbient.Service;
        }

        TransactionService transaction;

        lock (_lock)
        {
            if (_transactions.Count >= MAX_OPEN_TRANSACTIONS)
                throw new LiteException(0, "Maximum number of transactions reached");

            var initialSize = GetInitialSize();
            transaction = new TransactionService(_header, _locker, _disk, _walIndex, initialSize, this, queryOnly);
            _transactions[transaction.TransactionID] = transaction;
        }

        try
        {
            _locker.EnterTransactionSync(); // Phase 4 bridge: blocking wait
        }
        catch
        {
            lock (_lock)
            {
                FreePages += transaction.MaxTransactionSize;
                _transactions.Remove(transaction.TransactionID);
            }
            transaction.Dispose();
            throw;
        }

        isNew = true;
        return transaction;
    }

    // ── Release ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Release a transaction: dispose it, return its pages to the pool, and exit the transaction gate.
    /// </summary>
    public void ReleaseTransaction(TransactionService transaction)
    {
        transaction.Dispose();

        lock (_lock)
        {
            _transactions.Remove(transaction.TransactionID);
            FreePages += transaction.MaxTransactionSize;
        }

        _locker.ExitTransaction();
    }

    // ── Current-transaction ambient context ──────────────────────────────────

    /// <summary>
    /// Tracks the <see cref="TransactionService"/> that is currently executing on the
    /// active async call chain — covers both explicit (<see cref="LiteTransaction"/>) and
    /// auto-transactions created by <see cref="QueryExecutor"/>.
    ///
    /// Phase 6: replaces the old thread-local <c>GetThreadTransaction()</c> pattern
    /// (removed in Phase 2) for system collections that need to attach a read snapshot
    /// to the transaction that is already running the surrounding query.
    /// </summary>
    private static readonly AsyncLocal<TransactionService> _currentTransaction =
        new AsyncLocal<TransactionService>();

    /// <summary>
    /// Set the transaction that is currently driving the async execution context.
    /// Called by <see cref="QueryExecutor.ExecuteQueryCore"/> before the query pipeline
    /// runs and cleared (<c>null</c>) after it completes.
    /// </summary>
    internal static void SetCurrentTransaction(TransactionService transaction)
    {
        _currentTransaction.Value = transaction;
    }

    /// <summary>
    /// Returns the <see cref="TransactionService"/> that is currently active on this
    /// async execution context, or <c>null</c> if none has been set.
    ///
    /// Resolution order:
    /// <list type="number">
    ///   <item>Explicit ambient transaction (<see cref="LiteTransaction.CurrentAmbient"/>).</item>
    ///   <item>Auto-transaction set by <see cref="SetCurrentTransaction"/>.</item>
    /// </list>
    /// </summary>
    public TransactionService GetCurrentTransaction()
    {
        return LiteTransaction.CurrentAmbient?.Service ?? _currentTransaction.Value;
    }

    /// <summary>Legacy alias kept for call sites not yet updated.</summary>
    public TransactionService GetAmbientTransaction() => GetCurrentTransaction();

    // ── Page budget helpers ───────────────────────────────────────────────────

    private int GetInitialSize()
    {
        // called inside _lock
        if (FreePages >= InitialSize)
        {
            FreePages -= InitialSize;
            return InitialSize;
        }

        var sum = 0;

        foreach (var trans in _transactions.Values)
        {
            var reduce = trans.MaxTransactionSize / InitialSize;
            trans.MaxTransactionSize -= reduce;
            sum += reduce;
        }

        return sum;
    }

    private bool TryExtend(TransactionService trans)
    {
        lock (_lock)
        {
            if (FreePages >= InitialSize)
            {
                trans.MaxTransactionSize += InitialSize;
                FreePages -= InitialSize;
                return true;
            }

            return false;
        }
    }

    public bool CheckSafepoint(TransactionService trans)
    {
        return
            trans.Pages.TransactionSize >= trans.MaxTransactionSize &&
            !TryExtend(trans);
    }
}