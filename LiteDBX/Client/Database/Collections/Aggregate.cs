using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

public partial class LiteCollection<T>
{
    #region Count

    /// <summary>
    /// Get document count in collection
    /// </summary>
    public ValueTask<int> Count(CancellationToken cancellationToken = default)
        => Query().Count(cancellationToken);

    /// <summary>
    /// Get document count in collection using predicate filter expression
    /// </summary>
    public ValueTask<int> Count(BsonExpression predicate, CancellationToken cancellationToken = default)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        return Query().Where(predicate).Count(cancellationToken);
    }

    /// <summary>
    /// Get document count in collection using predicate filter expression
    /// </summary>
    public ValueTask<int> Count(string predicate, BsonDocument parameters, CancellationToken cancellationToken = default)
        => Count(BsonExpression.Create(predicate, parameters), cancellationToken);

    /// <summary>
    /// Get document count in collection using predicate filter expression
    /// </summary>
    public ValueTask<int> Count(string predicate, params BsonValue[] args)
        => Count(BsonExpression.Create(predicate, args));

    /// <summary>
    /// Count documents matching a query. This method does not deserialize any documents. Needs indexes on query expression
    /// </summary>
    public ValueTask<int> Count(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => Count(_mapper.GetExpression(predicate), cancellationToken);

    /// <summary>
    /// Get document count in collection using predicate filter expression
    /// </summary>
    public ValueTask<int> Count(Query query, CancellationToken cancellationToken = default)
        => new LiteQueryable<T>(_engine, _mapper, Name, query).Count(cancellationToken);

    #endregion

    #region LongCount

    /// <summary>
    /// Get document count in collection
    /// </summary>
    public ValueTask<long> LongCount(CancellationToken cancellationToken = default)
        => Query().LongCount(cancellationToken);

    /// <summary>
    /// Get document count in collection using predicate filter expression
    /// </summary>
    public ValueTask<long> LongCount(BsonExpression predicate, CancellationToken cancellationToken = default)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        return Query().Where(predicate).LongCount(cancellationToken);
    }

    /// <summary>
    /// Get document count in collection using predicate filter expression
    /// </summary>
    public ValueTask<long> LongCount(string predicate, BsonDocument parameters, CancellationToken cancellationToken = default)
        => LongCount(BsonExpression.Create(predicate, parameters), cancellationToken);

    /// <summary>
    /// Get document count in collection using predicate filter expression
    /// </summary>
    public ValueTask<long> LongCount(string predicate, params BsonValue[] args)
        => LongCount(BsonExpression.Create(predicate, args));

    /// <summary>
    /// Get document count in collection using predicate filter expression
    /// </summary>
    public ValueTask<long> LongCount(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => LongCount(_mapper.GetExpression(predicate), cancellationToken);

    /// <summary>
    /// Get document count in collection using predicate filter expression
    /// </summary>
    public ValueTask<long> LongCount(Query query, CancellationToken cancellationToken = default)
        => new LiteQueryable<T>(_engine, _mapper, Name, query).LongCount(cancellationToken);

    #endregion

    #region Exists

    /// <summary>
    /// Get true if collection contains at least 1 document that satisfies the predicate expression
    /// </summary>
    public ValueTask<bool> Exists(BsonExpression predicate, CancellationToken cancellationToken = default)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        return Query().Where(predicate).Exists(cancellationToken);
    }

    /// <summary>
    /// Get true if collection contains at least 1 document that satisfies the predicate expression
    /// </summary>
    public ValueTask<bool> Exists(string predicate, BsonDocument parameters, CancellationToken cancellationToken = default)
        => Exists(BsonExpression.Create(predicate, parameters), cancellationToken);

    /// <summary>
    /// Get true if collection contains at least 1 document that satisfies the predicate expression
    /// </summary>
    public ValueTask<bool> Exists(string predicate, params BsonValue[] args)
        => Exists(BsonExpression.Create(predicate, args));

    /// <summary>
    /// Get true if collection contains at least 1 document that satisfies the predicate expression
    /// </summary>
    public ValueTask<bool> Exists(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => Exists(_mapper.GetExpression(predicate), cancellationToken);

    /// <summary>
    /// Get true if collection contains at least 1 document that satisfies the predicate expression
    /// </summary>
    public ValueTask<bool> Exists(Query query, CancellationToken cancellationToken = default)
        => new LiteQueryable<T>(_engine, _mapper, Name, query).Exists(cancellationToken);

    #endregion

    #region Min / Max

    /// <summary>
    /// Returns the min value from specified key value in collection
    /// </summary>
    public async ValueTask<BsonValue> Min(BsonExpression keySelector, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(keySelector)) throw new ArgumentNullException(nameof(keySelector));

        // Select the key column, order ascending — first result is the minimum.
        var q = (ILiteQueryableResult<BsonDocument>)Query().OrderBy(keySelector).Select(keySelector);
        var doc = await q.First(cancellationToken).ConfigureAwait(false);
        return doc[doc.Keys.First()];
    }

    /// <summary>
    /// Returns the min value of _id index
    /// </summary>
    public ValueTask<BsonValue> Min(CancellationToken cancellationToken = default)
        => Min("_id", cancellationToken);

    /// <summary>
    /// Returns the min value from specified key value in collection
    /// </summary>
    public async ValueTask<K> Min<K>(Expression<Func<T, K>> keySelector, CancellationToken cancellationToken = default)
    {
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        var expr = _mapper.GetExpression(keySelector);
        var value = await Min(expr, cancellationToken).ConfigureAwait(false);
        return (K)_mapper.Deserialize(typeof(K), value);
    }

    /// <summary>
    /// Returns the max value from specified key value in collection
    /// </summary>
    public async ValueTask<BsonValue> Max(BsonExpression keySelector, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(keySelector)) throw new ArgumentNullException(nameof(keySelector));

        // Select the key column, order descending — first result is the maximum.
        var q = (ILiteQueryableResult<BsonDocument>)Query().OrderByDescending(keySelector).Select(keySelector);
        var doc = await q.First(cancellationToken).ConfigureAwait(false);
        return doc[doc.Keys.First()];
    }

    /// <summary>
    /// Returns the max _id index key value
    /// </summary>
    public ValueTask<BsonValue> Max(CancellationToken cancellationToken = default)
        => Max("_id", cancellationToken);

    /// <summary>
    /// Returns the last/max field using a linq expression
    /// </summary>
    public async ValueTask<K> Max<K>(Expression<Func<T, K>> keySelector, CancellationToken cancellationToken = default)
    {
        if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
        var expr = _mapper.GetExpression(keySelector);
        var value = await Max(expr, cancellationToken).ConfigureAwait(false);
        return (K)_mapper.Deserialize(typeof(K), value);
    }

    #endregion
}