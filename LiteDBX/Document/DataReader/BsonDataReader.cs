using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

/// <summary>
/// Reads a void, single, or async-stream sequence of <see cref="BsonValue"/> results.
///
/// Phase 4 redesign: replaced the synchronous <see cref="IEnumerator{T}"/> + <c>EngineState</c>
/// bridge with an <see cref="IAsyncEnumerator{T}"/> source. <see cref="Read"/> now genuinely awaits
/// the enumerator; the former <c>ReadSync</c> internal bridge and sync <c>Dispose</c> have been
/// removed.
///
/// Result abstraction decision (Phase 1 / Phase 4):
/// <c>IBsonDataReader</c> is retained as an explicit async cursor (rather than a raw
/// <c>IAsyncEnumerable&lt;BsonDocument&gt;</c>) because it carries <see cref="Collection"/>
/// context required by SQL-level <c>Execute</c> callers and supports field-level
/// access via <c>this[string field]</c>.
///
/// Disposal model:
/// For async-stream instances <see cref="DisposeAsync"/> awaits the enumerator's own
/// <c>DisposeAsync</c>, which propagates resource release up the pipeline
/// (e.g. releasing the query transaction held by <see cref="Engine.QueryExecutor"/>).
/// For single-value or empty instances disposal is a no-op <c>ValueTask</c>.
/// </summary>
public class BsonDataReader : IBsonDataReader
{
    private readonly IAsyncEnumerator<BsonDocument> _enumerator;
    private bool _disposed;
    private bool _isFirst; // single-value "prime" sentinel

    // ── Constructors ──────────────────────────────────────────────────────────

    /// <summary>Initialize with no value (void result).</summary>
    internal BsonDataReader()
    {
        HasValues = false;
    }

    /// <summary>Initialize with a single pre-computed value.</summary>
    internal BsonDataReader(BsonValue value, string collection = null)
    {
        Current = value;
        _isFirst = HasValues = true;
        Collection = collection;
    }

    /// <summary>
    /// Initialize with an async document stream.
    ///
    /// The provided <paramref name="cancellationToken"/> is bound to the enumerator at construction
    /// via <see cref="IAsyncEnumerable{T}.GetAsyncEnumerator"/> so the same token governs both
    /// advancing and cancelling the stream.
    ///
    /// <see cref="HasValues"/> is set optimistically to <c>true</c>; callers must
    /// <see cref="Read"/> to discover emptiness, consistent with a forward-only cursor model.
    /// </summary>
    internal BsonDataReader(
        IAsyncEnumerable<BsonDocument> source,
        string collection,
        CancellationToken cancellationToken = default)
    {
        Collection = collection;
        _enumerator = source.GetAsyncEnumerator(cancellationToken);
        HasValues = true; // optimistic — caller must Read() to discover emptiness
    }

    // ── IBsonDataReader ───────────────────────────────────────────────────────

    public bool HasValues { get; private set; }
    public BsonValue Current { get; private set; }
    public string Collection { get; }

    public BsonValue this[string field] => Current?.AsDocument?[field] ?? BsonValue.Null;

    /// <summary>
    /// Advance the cursor to the next result.
    ///
    /// For async-stream instances, genuinely awaits the underlying <see cref="IAsyncEnumerator{T}"/>.
    /// For single-value instances, returns a completed <see cref="ValueTask{T}"/>.
    /// The <paramref name="cancellationToken"/> parameter is accepted for interface compatibility;
    /// for async-stream instances the token is already bound to the enumerator at construction time.
    /// </summary>
    public async ValueTask<bool> Read(CancellationToken cancellationToken = default)
    {
        if (!HasValues) return false;

        // Single-value path — _enumerator is null for single-value and empty instances.
        if (_enumerator == null)
        {
            if (_isFirst)
            {
                _isFirst = false;
                return true;
            }

            HasValues = false;
            return false;
        }

        // Async-stream path.
        if (await _enumerator.MoveNextAsync().ConfigureAwait(false))
        {
            Current = _enumerator.Current;
            return true;
        }

        HasValues = false;
        return false;
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_enumerator != null)
        {
            await _enumerator.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }
}