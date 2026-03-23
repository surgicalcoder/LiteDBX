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
/// Phase 6: <see cref="SharedEngine"/> now uses <see cref="System.Threading.SemaphoreSlim.WaitAsync"/>
/// instead of the blocking OS Mutex. The async dispose callback correctly releases
/// the semaphore via <see cref="System.Threading.SemaphoreSlim.Release"/> without blocking threads.
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