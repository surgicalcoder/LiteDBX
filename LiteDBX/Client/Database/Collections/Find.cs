using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

public partial class LiteCollection<T>
{
    /// <summary>
    /// Return a new LiteQueryable to build more complex queries.
    /// </summary>
    public ILiteQueryable<T> Query()
    {
        return new LiteQueryable<T>(_engine, _mapper, Name, new Query()).Include(_includes);
    }

    #region Find

    /// <summary>Stream documents matching a BsonExpression predicate.</summary>
    public IAsyncEnumerable<T> Find(
        BsonExpression predicate,
        int skip = 0,
        int limit = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        return Query()
            .Include(_includes)
            .Where(predicate)
            .Skip(skip)
            .Limit(limit)
            .ToEnumerable(cancellationToken);
    }

    /// <summary>Stream documents matching a structured <see cref="Query"/>.</summary>
    public IAsyncEnumerable<T> Find(
        Query query,
        int skip = 0,
        int limit = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        if (query == null) throw new ArgumentNullException(nameof(query));

        if (skip != 0) query.Offset = skip;
        if (limit != int.MaxValue) query.Limit = limit;

        return new LiteQueryable<T>(_engine, _mapper, Name, query).ToEnumerable(cancellationToken);
    }

    /// <summary>Stream documents matching a LINQ predicate.</summary>
    public IAsyncEnumerable<T> Find(
        Expression<Func<T, bool>> predicate,
        int skip = 0,
        int limit = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        return Find(_mapper.GetExpression(predicate), skip, limit, cancellationToken);
    }

    #endregion

    #region FindById / FindOne / FindAll

    /// <summary>Find a single document by its <c>_id</c>. Returns <c>null</c> if not found.</summary>
    public ValueTask<T> FindById(BsonValue id, CancellationToken cancellationToken = default)
    {
        if (id == null || id.IsNull) throw new ArgumentNullException(nameof(id));

        return Query()
            .Where(BsonExpression.Create("_id = @0", id))
            .FirstOrDefault(cancellationToken);
    }

    /// <summary>Find the first document matching a BsonExpression. Returns <c>null</c> if not found.</summary>
    public ValueTask<T> FindOne(BsonExpression predicate, CancellationToken cancellationToken = default)
    {
        return Query().Where(predicate).FirstOrDefault(cancellationToken);
    }

    /// <summary>Find the first document matching a parameterised predicate. Returns <c>null</c> if not found.</summary>
    public ValueTask<T> FindOne(string predicate, BsonDocument parameters, CancellationToken cancellationToken = default)
    {
        return FindOne(BsonExpression.Create(predicate, parameters), cancellationToken);
    }

    /// <summary>Find the first document matching a predicate with positional args. Returns <c>null</c> if not found.</summary>
    public ValueTask<T> FindOne(BsonExpression predicate, params BsonValue[] args)
    {
        return FindOne(BsonExpression.Create(predicate, args));
    }

    /// <summary>Find the first document matching a LINQ predicate. Returns <c>null</c> if not found.</summary>
    public ValueTask<T> FindOne(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return FindOne(_mapper.GetExpression(predicate), cancellationToken);
    }

    /// <summary>Find the first document matching a structured <see cref="Query"/>. Returns <c>null</c> if not found.</summary>
    public ValueTask<T> FindOne(Query query, CancellationToken cancellationToken = default)
    {
        return new LiteQueryable<T>(_engine, _mapper, Name, query).FirstOrDefault(cancellationToken);
    }

    /// <summary>Stream all documents in this collection, ordered by <c>_id</c>.</summary>
    public IAsyncEnumerable<T> FindAll(CancellationToken cancellationToken = default)
    {
        return Query().Include(_includes).ToEnumerable(cancellationToken);
    }

    #endregion
}