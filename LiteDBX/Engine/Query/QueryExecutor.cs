using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteDbX.Utils.Extensions;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

/// <summary>
/// Executes a <see cref="QueryPlan"/> and streams matching documents.
///
/// Phase 4 redesign:
/// <list type="bullet">
///   <item><see cref="ExecuteQuery"/> now returns <see cref="IAsyncEnumerable{BsonDocument}"/>.</item>
///   <item>Transaction acquisition uses <see cref="TransactionMonitor.GetOrCreateTransactionAsync"/>
///     — the blocking <c>GetOrCreateTransactionSync</c> bridge is no longer used here.</item>
///   <item>The inner document pipeline (<see cref="BasePipe.Pipe"/>) remains synchronous because all
///     its steps are CPU-bound transforms over the in-memory page cache. Async I/O is not required
///     at the per-document level; only the transaction lock entry is a genuine async boundary.</item>
/// </list>
///
/// WAL read lock note: <see cref="WalIndexService"/> still uses
/// <c>EnterReadSync</c> inside <c>CreateSnapshot</c>. Converting that to async is deferred to
/// Phase 3 / Phase 6 (disk and shared-mode redesign).
/// </summary>
internal class QueryExecutor
{
    private readonly string _collection;
    private readonly CursorInfo _cursor;
    private readonly DiskService _disk;
    private readonly LiteEngine _engine;
    private readonly TransactionMonitor _monitor;
    private readonly EnginePragmas _pragmas;
    private readonly Query _query;
    private readonly SortDisk _sortDisk;
    private readonly Func<CancellationToken, ValueTask<IEnumerable<BsonDocument>>> _sourceFactory;
    private readonly EngineState _state;

    internal static QueryExecutor Create(
        LiteEngine engine,
        EngineState state,
        TransactionMonitor monitor,
        SortDisk sortDisk,
        DiskService disk,
        EnginePragmas pragmas,
        string collection,
        Query query,
        Func<CancellationToken, ValueTask<IEnumerable<BsonDocument>>> sourceFactory,
        TransactionService transaction = null)
        => new QueryExecutor(engine, state, monitor, sortDisk, disk, pragmas, collection, query, sourceFactory, transaction);

    public QueryExecutor(
        LiteEngine engine,
        EngineState state,
        TransactionMonitor monitor,
        SortDisk sortDisk,
        DiskService disk,
        EnginePragmas pragmas,
        string collection,
        Query query,
        Func<CancellationToken, ValueTask<IEnumerable<BsonDocument>>> sourceFactory,
        TransactionService transaction = null)
    {
        _engine = engine;
        _state = state;
        _monitor = monitor;
        _sortDisk = sortDisk;
        _disk = disk;
        _pragmas = pragmas;
        _collection = collection;
        _query = query;
        _cursor = new CursorInfo(collection, query);
        _sourceFactory = sourceFactory;
        BoundTransaction = transaction;

        LOG(_query.ToSQL(_collection).Replace(Environment.NewLine, " "), "QUERY");
    }

    internal TransactionService BoundTransaction { get; set; }

    // ── Public entry point ─────────────────────────────────────────────────────

    /// <summary>
    /// Stream query results as an <see cref="IAsyncEnumerable{BsonDocument}"/>.
    ///
    /// If <see cref="Query.Into"/> is set, all source documents are buffered, inserted into the
    /// target collection, and a single <c>{ count: N }</c> document is yielded.
    /// Otherwise matching documents are yielded directly from the CPU pipeline.
    /// </summary>
    public IAsyncEnumerable<BsonDocument> ExecuteQuery(CancellationToken cancellationToken = default)
    {
        return _query.Into != null
            ? ExecuteQueryInto(_query.Into, _query.IntoAutoId, cancellationToken)
            : ExecuteQueryCore(_query.ExplainPlan, cancellationToken);
    }

    // ── Core async iterator ────────────────────────────────────────────────────

    /// <summary>
    /// Acquire a transaction (async, non-blocking), execute the synchronous CPU pipeline,
    /// and release the transaction when enumeration is complete or abandoned.
    ///
    /// Phase 6: <see cref="TransactionMonitor.SetCurrentTransaction"/> is called before
    /// the pipeline so that system collections executed as a source (e.g. <c>$dump</c>,
    /// <c>$indexes</c>, <c>$page_list</c>) can retrieve the running transaction via
    /// <see cref="TransactionMonitor.GetCurrentTransaction"/> instead of the removed
    /// thread-local <c>GetThreadTransaction()</c>.
    /// </summary>
    private async IAsyncEnumerable<BsonDocument> ExecuteQueryCore(
        bool executionPlan,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        TransactionService transaction;
        bool isNew;
        var releaseTransaction = true;

        if (BoundTransaction != null)
        {
            transaction = BoundTransaction;
            isNew = false;
        }
        else
        {
            (transaction, isNew) = await _monitor
                .GetOrCreateTransactionAsync(true, cancellationToken)
                .ConfigureAwait(false);
        }

        // Publish the transaction to synchronous system-collection sources running in this context.
        TransactionMonitor.SetCurrentTransaction(transaction);

        transaction.OpenCursors.Add(_cursor);

        var source = await MaterializeSourceAsync(cancellationToken).ConfigureAwait(false);

        using var docs = RunQuery(transaction, source, executionPlan).GetEnumerator();

        try
        {
            while (true)
            {
                BsonDocument doc;

                try
                {
                    if (!docs.MoveNext())
                    {
                        break;
                    }

                    doc = docs.Current;
                }
                catch (Exception ex)
                {
                    if (!_state.Handle(ex))
                    {
                        releaseTransaction = false;
                    }

                    throw;
                }

                cancellationToken.ThrowIfCancellationRequested();
                yield return doc;
            }
        }
        finally
        {
            TransactionMonitor.SetCurrentTransaction(null);
            transaction.OpenCursors.Remove(_cursor);

            if (isNew && releaseTransaction)
            {
                _monitor.ReleaseTransaction(transaction);
            }
        }
    }

    private async ValueTask<IEnumerable<BsonDocument>> MaterializeSourceAsync(CancellationToken cancellationToken)
    {
        if (_sourceFactory == null)
        {
            return null;
        }

        return await _sourceFactory(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Execute the query, buffer all source documents, insert them into <paramref name="into"/>,
    /// then yield a single <c>{ count: N }</c> document.
    ///
    /// Transaction lifecycle: the query transaction is acquired and fully released inside
    /// <see cref="ExecuteQueryCore"/>. The insert runs under a separate auto-transaction via
    /// the engine insert path, so the two transactions are sequential, not nested.
    /// </summary>
    private async IAsyncEnumerable<BsonDocument> ExecuteQueryInto(
        string into,
        BsonAutoId autoId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new List<BsonDocument>();

        await foreach (var doc in ExecuteQueryCore(false, cancellationToken).ConfigureAwait(false))
        {
            buffer.Add(doc);
        }

        int count;

        if (into.StartsWith("$"))
        {
            SqlParser.ParseCollection(new Tokenizer(into), out var name, out var options);
            var sys = _engine.GetSystemCollection(name);
            count = sys.Output(buffer, options);
        }
        else
        {
            count = await _engine
                .Insert(into, buffer, autoId, cancellationToken)
                .ConfigureAwait(false);
        }

        yield return new BsonDocument { ["count"] = count };
    }

    // ── Synchronous CPU pipeline ───────────────────────────────────────────────
    // All steps below are pure in-memory transforms over the page cache; no I/O occurs here.

    private IEnumerable<BsonDocument> RunQuery(
        TransactionService transaction,
        IEnumerable<BsonDocument> source,
        bool executionPlan)
    {
        var snapshot = transaction.CreateSnapshot(
            _query.ForUpdate ? LockMode.Write : LockMode.Read,
            _collection,
            false);

        // no collection and no external source — handle SELECT without FROM
        if (snapshot.CollectionPage == null && source == null)
        {
            if (_query.Select.UseSource)
            {
                yield return _query.Select.ExecuteScalar(_pragmas.Collation).AsDocument;
            }

            yield break;
        }

        var optimizer = new QueryOptimization(snapshot, _query, source, _pragmas.Collation);
        var queryPlan = optimizer.ProcessQuery();

        if (executionPlan)
        {
            yield return queryPlan.GetExecutionPlan();
            yield break;
        }

        if (queryPlan.IsEmptyResult)
        {
            yield break;
        }

        var nodes = queryPlan.Index.Run(
            snapshot.CollectionPage,
            new IndexService(snapshot, _pragmas.Collation, _disk.MAX_ITEMS_COUNT));

        var pipe = queryPlan.GetPipe(
            transaction,
            snapshot,
            _sortDisk,
            _pragmas,
            _disk.MAX_ITEMS_COUNT,
            _state.ReadTransform);

        using var _ = _cursor.Elapsed.StartDisposable();

        foreach (var doc in pipe.Pipe(nodes, queryPlan))
        {
            _state.Validate();

            if (transaction.State != TransactionState.Active)
            {
                throw new LiteException(0,
                    $"There is no more active transaction for this cursor: " +
                    $"{_cursor.Query.ToSQL(_cursor.Collection)}");
            }

            _cursor.Elapsed.Stop();
            yield return doc;
            _cursor.Elapsed.Start();
        }
    }
}