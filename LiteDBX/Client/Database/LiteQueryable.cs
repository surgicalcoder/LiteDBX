using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteDbX.Engine;

namespace LiteDbX;

/// <summary>
/// Fluent async query builder for a typed collection.
///
/// Composition methods (Where, OrderBy, Include, Select, Offset, Limit, ForUpdate, GroupBy, Having)
/// are synchronous — they only mutate an in-memory <see cref="Query"/> plan with no I/O.
///
/// Terminal operations (ToEnumerable, ToList, ToArray, First, FirstOrDefault, Single,
/// SingleOrDefault, Count, LongCount, Exists, Into, ExecuteReader, GetPlan) are async-only.
///
/// Phase 4: all terminal methods replaced with genuine async implementations.
/// The sync bridge (<c>ExecuteReader()</c>, <c>ToList()</c>, etc.) has been removed.
/// </summary>
public class LiteQueryable<T> : ILiteQueryable<T>
{
    protected readonly string _collection;
    protected readonly ILiteEngine _engine;
    private readonly bool _isSimpleType = Reflection.IsSimpleType(typeof(T));
    protected readonly BsonMapper _mapper;
    protected readonly Query _query;

    internal LiteQueryable(ILiteEngine engine, BsonMapper mapper, string collection, Query query)
    {
        _engine = engine;
        _mapper = mapper;
        _collection = collection;
        _query = query;
    }

    // ── Composition (synchronous — no I/O) ────────────────────────────────────

    #region GroupBy / Having

    public ILiteQueryable<T> GroupBy(BsonExpression keySelector)
    {
        if (_query.GroupBy != null) throw new ArgumentException("GROUP BY already defined in this query");
        _query.GroupBy = keySelector;
        return this;
    }

    public ILiteQueryable<T> Having(BsonExpression predicate)
    {
        if (_query.Having != null) throw new ArgumentException("HAVING already defined in this query");
        _query.Having = predicate;
        return this;
    }

    #endregion

    #region Include

    public ILiteQueryable<T> Include<K>(Expression<Func<T, K>> path)
    {
        _query.Includes.Add(_mapper.GetExpression(path));
        return this;
    }

    public ILiteQueryable<T> Include(BsonExpression path)
    {
        _query.Includes.Add(path);
        return this;
    }

    public ILiteQueryable<T> Include(List<BsonExpression> paths)
    {
        _query.Includes.AddRange(paths);
        return this;
    }

    #endregion

    #region Where

    public ILiteQueryable<T> Where(BsonExpression predicate)
    {
        _query.Where.Add(predicate);
        return this;
    }

    public ILiteQueryable<T> Where(string predicate, BsonDocument parameters)
    {
        _query.Where.Add(BsonExpression.Create(predicate, parameters));
        return this;
    }

    public ILiteQueryable<T> Where(string predicate, params BsonValue[] args)
    {
        _query.Where.Add(BsonExpression.Create(predicate, args));
        return this;
    }

    public ILiteQueryable<T> Where(Expression<Func<T, bool>> predicate)
    {
        return Where(_mapper.GetExpression(predicate));
    }

    #endregion

    #region OrderBy

    public ILiteQueryable<T> OrderBy(BsonExpression keySelector, int order = Query.Ascending)
    {
        if (_query.OrderBy != null) throw new ArgumentException("ORDER BY already defined in this query builder");
        _query.OrderBy = keySelector;
        _query.Order = order;
        return this;
    }

    public ILiteQueryable<T> OrderBy<K>(Expression<Func<T, K>> keySelector, int order = Query.Ascending)
    {
        return OrderBy(_mapper.GetExpression(keySelector), order);
    }

    public ILiteQueryable<T> OrderByDescending(BsonExpression keySelector) => OrderBy(keySelector, Query.Descending);

    public ILiteQueryable<T> OrderByDescending<K>(Expression<Func<T, K>> keySelector) => OrderBy(keySelector, Query.Descending);

    #endregion

    #region Select

    public ILiteQueryableResult<BsonDocument> Select(BsonExpression selector)
    {
        _query.Select = selector;
        return new LiteQueryable<BsonDocument>(_engine, _mapper, _collection, _query);
    }

    public ILiteQueryableResult<K> Select<K>(Expression<Func<T, K>> selector)
    {
        if (_query.GroupBy != null) throw new ArgumentException("Use Select(BsonExpression selector) when using GroupBy query");
        _query.Select = _mapper.GetExpression(selector);
        return new LiteQueryable<K>(_engine, _mapper, _collection, _query);
    }

    #endregion

    #region Offset / Limit / ForUpdate

    public ILiteQueryableResult<T> ForUpdate() { _query.ForUpdate = true; return this; }

    public ILiteQueryableResult<T> Offset(int offset) { _query.Offset = offset; return this; }

    public ILiteQueryableResult<T> Skip(int offset) => Offset(offset);

    public ILiteQueryableResult<T> Limit(int limit) { _query.Limit = limit; return this; }

    #endregion

    // ── Terminal operations (async-only — touch storage) ─────────────────────

    #region ExecuteReader

    /// <summary>
    /// Return an async cursor over the raw result set.
    /// The cursor wraps the <see cref="IAsyncEnumerable{T}"/> returned by the engine so that
    /// field-level access and collection context are preserved on the <see cref="IBsonDataReader"/>
    /// interface.
    /// </summary>
    public ValueTask<IBsonDataReader> ExecuteReader(CancellationToken cancellationToken = default)
    {
        _query.ExplainPlan = false;
        var stream = _engine.Query(_collection, _query, cancellationToken);
        IBsonDataReader reader = new BsonDataReader(stream, _collection, cancellationToken);
        return new ValueTask<IBsonDataReader>(reader);
    }

    #endregion

    #region Streaming results

    /// <summary>Stream results as <see cref="BsonDocument"/> values.</summary>
    public async IAsyncEnumerable<BsonDocument> ToDocuments(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _query.ExplainPlan = false;

        await foreach (var doc in _engine.Query(_collection, _query, cancellationToken).ConfigureAwait(false))
        {
            yield return doc;
        }
    }

    /// <summary>
    /// Stream results as <typeparamref name="T"/> values, applying the BsonMapper.
    /// For simple types (string, int, etc.) the first field of each result document is used.
    /// </summary>
    public async IAsyncEnumerable<T> ToEnumerable(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _query.ExplainPlan = false;

        if (_isSimpleType)
        {
            await foreach (var doc in _engine.Query(_collection, _query, cancellationToken).ConfigureAwait(false))
            {
                var val = doc[doc.Keys.First()];
                yield return (T)_mapper.Deserialize(typeof(T), val);
            }
        }
        else
        {
            await foreach (var doc in _engine.Query(_collection, _query, cancellationToken).ConfigureAwait(false))
            {
                yield return (T)_mapper.Deserialize(typeof(T), doc);
            }
        }
    }

    #endregion

    #region Materializers

    public async ValueTask<List<T>> ToList(CancellationToken cancellationToken = default)
    {
        var list = new List<T>();
        await foreach (var item in ToEnumerable(cancellationToken).ConfigureAwait(false))
        {
            list.Add(item);
        }
        return list;
    }

    public async ValueTask<T[]> ToArray(CancellationToken cancellationToken = default)
    {
        return (await ToList(cancellationToken).ConfigureAwait(false)).ToArray();
    }

    #endregion

    #region Single / First

    public async ValueTask<T> First(CancellationToken cancellationToken = default)
    {
        await foreach (var item in ToEnumerable(cancellationToken).ConfigureAwait(false))
        {
            return item;
        }
        throw new InvalidOperationException("Sequence contains no elements.");
    }

    public async ValueTask<T> FirstOrDefault(CancellationToken cancellationToken = default)
    {
        await foreach (var item in ToEnumerable(cancellationToken).ConfigureAwait(false))
        {
            return item;
        }
        return default;
    }

    public async ValueTask<T> Single(CancellationToken cancellationToken = default)
    {
        await using var en = ToEnumerable(cancellationToken).GetAsyncEnumerator(cancellationToken);

        if (!await en.MoveNextAsync().ConfigureAwait(false))
            throw new InvalidOperationException("Sequence contains no elements.");

        var first = en.Current;

        if (await en.MoveNextAsync().ConfigureAwait(false))
            throw new InvalidOperationException("Sequence contains more than one element.");

        return first;
    }

    public async ValueTask<T> SingleOrDefault(CancellationToken cancellationToken = default)
    {
        await using var en = ToEnumerable(cancellationToken).GetAsyncEnumerator(cancellationToken);

        if (!await en.MoveNextAsync().ConfigureAwait(false))
            return default;

        var first = en.Current;

        if (await en.MoveNextAsync().ConfigureAwait(false))
            throw new InvalidOperationException("Sequence contains more than one element.");

        return first;
    }

    #endregion

    #region Aggregate (Count / LongCount / Exists)

    // These use the aggregate SELECT pattern: temporarily replace _query.Select with an
    // aggregation expression, execute, then restore the original. The _query instance is
    // shared across LiteQueryable<T> instances that were created via Select(...), so the
    // save/restore pattern is necessary for correctness.

    public async ValueTask<int> Count(CancellationToken cancellationToken = default)
    {
        var savedSelect = _query.Select;
        try
        {
            Select("{ count: COUNT(*._id) }");
            await foreach (var doc in _engine.Query(_collection, _query, cancellationToken).ConfigureAwait(false))
            {
                return doc["count"].AsInt32;
            }
            return 0;
        }
        finally
        {
            _query.Select = savedSelect;
        }
    }

    public async ValueTask<long> LongCount(CancellationToken cancellationToken = default)
    {
        var savedSelect = _query.Select;
        try
        {
            Select("{ count: COUNT(*._id) }");
            await foreach (var doc in _engine.Query(_collection, _query, cancellationToken).ConfigureAwait(false))
            {
                return doc["count"].AsInt64;
            }
            return 0L;
        }
        finally
        {
            _query.Select = savedSelect;
        }
    }

    public async ValueTask<bool> Exists(CancellationToken cancellationToken = default)
    {
        var savedSelect = _query.Select;
        try
        {
            Select("{ exists: ANY(*._id) }");
            await foreach (var doc in _engine.Query(_collection, _query, cancellationToken).ConfigureAwait(false))
            {
                return doc["exists"].AsBoolean;
            }
            return false;
        }
        finally
        {
            _query.Select = savedSelect;
        }
    }

    #endregion

    #region Into

    /// <summary>
    /// Insert all results into <paramref name="newCollection"/>.
    /// Returns the number of documents inserted.
    ///
    /// The Into logic runs inside <see cref="QueryExecutor"/>: documents are buffered, then
    /// inserted under a separate auto-transaction, and a single <c>{ count: N }</c> document
    /// is yielded back.
    /// </summary>
    public async ValueTask<int> Into(
        string newCollection,
        BsonAutoId autoId = BsonAutoId.ObjectId,
        CancellationToken cancellationToken = default)
    {
        _query.Into = newCollection;
        _query.IntoAutoId = autoId;
        _query.ExplainPlan = false;

        await foreach (var doc in _engine.Query(_collection, _query, cancellationToken).ConfigureAwait(false))
        {
            return doc["count"].AsInt32;
        }

        return 0;
    }

    #endregion

    #region GetPlan

    /// <summary>
    /// Execute the query with <c>ExplainPlan = true</c> and return the execution plan document.
    /// The ExplainPlan flag is reset to false after execution.
    /// </summary>
    public async ValueTask<BsonDocument> GetPlan(CancellationToken cancellationToken = default)
    {
        _query.ExplainPlan = true;
        try
        {
            await foreach (var doc in _engine.Query(_collection, _query, cancellationToken).ConfigureAwait(false))
            {
                return doc;
            }
            return null;
        }
        finally
        {
            _query.ExplainPlan = false;
        }
    }

    #endregion
}

