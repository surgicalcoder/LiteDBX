using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

/// <summary>
/// Wraps an <see cref="IBsonDataReader"/> and invokes a callback when the reader is disposed.
/// Used by <see cref="SharedEngine"/> to release the inter-process mutex after query results
/// have been fully consumed.
///
/// Phase 4: Updated to implement the async <see cref="IBsonDataReader"/> contract
/// (<see cref="Read"/> returning <c>ValueTask&lt;bool&gt;</c>, <see cref="DisposeAsync"/>).
///
/// Phase 6 note: The <see cref="SharedEngine"/> mutex acquisition/release strategy
/// is deferred to Phase 6 (Shared Mode redesign). The dispose callback invoked here
/// still releases the mutex synchronously inside the async path. This is acceptable
/// as a bridge until Phase 6 introduces async-safe shared-mode coordination.
/// </summary>
public class SharedDataReader : IBsonDataReader
{
    private readonly Func<ValueTask> _disposeAsync;
    private readonly IBsonDataReader _reader;
    private bool _disposed;

    /// <summary>
    /// Construct a <see cref="SharedDataReader"/> with an async dispose callback.
    /// </summary>
    public SharedDataReader(IBsonDataReader reader, Func<ValueTask> disposeAsync)
    {
        _reader = reader;
        _disposeAsync = disposeAsync;
    }

    /// <summary>
    /// Convenience constructor for callers that have a synchronous dispose action.
    /// The action is wrapped in a completed <see cref="ValueTask"/>.
    ///
    /// Phase 6 bridge: synchronous action callers (SharedEngine) use this overload
    /// until the mutex release is redesigned to be async.
    /// </summary>
    public SharedDataReader(IBsonDataReader reader, Action dispose)
        : this(reader, () => { dispose(); return default; })
    {
    }

    public BsonValue this[string field] => _reader[field];
    public string Collection => _reader.Collection;
    public BsonValue Current => _reader.Current;
    public bool HasValues => _reader.HasValues;

    public ValueTask<bool> Read(CancellationToken cancellationToken = default)
        => _reader.Read(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _reader.DisposeAsync().ConfigureAwait(false);
        await _disposeAsync().ConfigureAwait(false);

        GC.SuppressFinalize(this);
    }
}