using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Engine;

/// <summary>
/// Concrete implementation of <see cref="ILiteTransaction"/>.
///
/// Phase 2 — transaction–operation association uses <see cref="AsyncLocal{T}"/> ambient context.
/// Phase 3 — <see cref="Commit"/>, <see cref="Rollback"/>, and <see cref="DisposeAsync"/> now
///   delegate to <see cref="TransactionService.CommitAsync"/> and
///   <see cref="TransactionService.RollbackAsync"/> so the WAL write is genuinely async rather
///   than a synchronous bridge.
/// </summary>
internal sealed class LiteTransaction : ILiteTransaction
{
    private static readonly AsyncLocal<LiteTransaction> _currentAmbient = new();

    private readonly TransactionMonitor _monitor;
    private readonly TransactionService _service;
    private bool _disposed;

    internal LiteTransaction(TransactionService service, TransactionMonitor monitor)
    {
        _service = service;
        _monitor = monitor;
        _currentAmbient.Value = this;
    }

    // ── Ambient context ───────────────────────────────────────────────────────

    /// <summary>
    /// The active explicit transaction for this async execution context,
    /// or <c>null</c> if no explicit transaction has been started.
    /// </summary>
    public static LiteTransaction CurrentAmbient => _currentAmbient.Value;

    /// <summary>Returns <c>true</c> if an explicit transaction is active in this async context.</summary>
    public static bool HasActive => _currentAmbient.Value != null;

    // ── Internal access ───────────────────────────────────────────────────────

    internal TransactionService Service => _service;
    internal TransactionMonitor Monitor => _monitor;

    // ── ILiteTransaction ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public ValueTask Commit(CancellationToken cancellationToken = default)
    {
        if (_service.State == TransactionState.Active)
            return _service.CommitAsync(cancellationToken);

        return default;
    }

    /// <inheritdoc/>
    public ValueTask Rollback(CancellationToken cancellationToken = default)
    {
        if (_service.State == TransactionState.Active)
            return _service.RollbackAsync(cancellationToken);

        return default;
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    /// <summary>
    /// Dispose the transaction. If <see cref="Commit"/> was not called, performs an implicit rollback.
    /// Clears the ambient context for the current async execution flow.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_service.State == TransactionState.Active)
            await _service.RollbackAsync().ConfigureAwait(false);

        _monitor.ReleaseTransaction(_service);
        _currentAmbient.Value = null;
    }
}
