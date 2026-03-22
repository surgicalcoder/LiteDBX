using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace LiteDbX.Engine;

public partial class LiteEngine
{
    /// <summary>
    /// Run a query over a collection and stream matching documents as an <see cref="IAsyncEnumerable{T}"/>.
    ///
    /// Phase 4: the Phase 2/3 bridge notes are retired. <see cref="QueryExecutor.ExecuteQuery"/>
    /// now uses <see cref="TransactionMonitor.GetOrCreateTransactionAsync"/> — no blocking wait
    /// occurs at transaction entry.
    ///
    /// Cursor lifetime guarantee:
    /// The <see cref="QueryExecutor"/> holds the transaction gate slot for the duration of
    /// enumeration. When the caller breaks early, cancels, or fully consumes the sequence the
    /// <c>finally</c> block inside <see cref="QueryExecutor.ExecuteQueryCore"/> releases the
    /// cursor registration and returns the transaction to the monitor.
    /// </summary>
    public IAsyncEnumerable<BsonDocument> Query(
        string collection,
        Query query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(collection))
            throw new ArgumentNullException(nameof(collection));
        if (query == null)
            throw new ArgumentNullException(nameof(query));

        _state.Validate();

        IEnumerable<BsonDocument> source = null;

        // Resolve system collections ($) before entering the async iterator so
        // argument validation runs eagerly rather than on first MoveNextAsync.
        if (collection.StartsWith("$"))
        {
            SqlParser.ParseCollection(new Tokenizer(collection), out var name, out var options);
            var sys = GetSystemCollection(name);
            source = sys.Input(options);
            collection = sys.Name;
        }

        return QueryCore(collection, query, source, cancellationToken);
    }

    private async IAsyncEnumerable<BsonDocument> QueryCore(
        string collection,
        Query query,
        IEnumerable<BsonDocument> source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var exec = new QueryExecutor(
            this, _state, _monitor, _sortDisk, _disk, _header.Pragmas,
            collection, query, source);

        await foreach (var doc in exec.ExecuteQuery(cancellationToken).ConfigureAwait(false))
        {
            yield return doc;
        }
    }
}
