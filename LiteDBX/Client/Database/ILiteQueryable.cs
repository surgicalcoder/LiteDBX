using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

/// <summary>
/// Fluent async query builder for a typed collection.
///
/// Composition methods are synchronous (no I/O; they build an expression plan).
/// Execution is triggered by the terminal operations defined on <see cref="ILiteQueryableResult{T}"/>,
/// all of which are async-only.
/// </summary>
public interface ILiteQueryable<T> : ILiteQueryableResult<T>
{
    // ── Include ───────────────────────────────────────────────────────────────
    ILiteQueryable<T> Include(BsonExpression path);
    ILiteQueryable<T> Include(List<BsonExpression> paths);
    ILiteQueryable<T> Include<K>(Expression<Func<T, K>> path);

    // ── Filter ────────────────────────────────────────────────────────────────
    ILiteQueryable<T> Where(BsonExpression predicate);
    ILiteQueryable<T> Where(string predicate, BsonDocument parameters);
    ILiteQueryable<T> Where(string predicate, params BsonValue[] args);
    ILiteQueryable<T> Where(Expression<Func<T, bool>> predicate);

    // ── Order ─────────────────────────────────────────────────────────────────
    ILiteQueryable<T> OrderBy(BsonExpression keySelector, int order = 1);
    ILiteQueryable<T> OrderBy<K>(Expression<Func<T, K>> keySelector, int order = 1);
    ILiteQueryable<T> OrderByDescending(BsonExpression keySelector);
    ILiteQueryable<T> OrderByDescending<K>(Expression<Func<T, K>> keySelector);

    // ── Group ─────────────────────────────────────────────────────────────────
    ILiteQueryable<T> GroupBy(BsonExpression keySelector);
    ILiteQueryable<T> Having(BsonExpression predicate);

    // ── Projection ────────────────────────────────────────────────────────────
    ILiteQueryableResult<BsonDocument> Select(BsonExpression selector);
    ILiteQueryableResult<K> Select<K>(Expression<Func<T, K>> selector);
}

/// <summary>
/// Terminal execution surface for a composed query.
///
/// Paging and hint methods are synchronous (pure plan modification).
/// All materialising and streaming operations are async-only — they hit storage.
/// </summary>
public interface ILiteQueryableResult<T>
{
    // ── Paging / hints (sync — plan only) ────────────────────────────────────
    ILiteQueryableResult<T> Limit(int limit);
    ILiteQueryableResult<T> Skip(int offset);
    ILiteQueryableResult<T> Offset(int offset);
    ILiteQueryableResult<T> ForUpdate();

    /// <summary>Return the query execution plan without running the query.</summary>
    ValueTask<BsonDocument> GetPlan(CancellationToken cancellationToken = default);

    // ── Execution (async-only) ────────────────────────────────────────────────

    /// <summary>Return an async cursor over raw <see cref="BsonDocument"/> results.</summary>
    ValueTask<IBsonDataReader> ExecuteReader(CancellationToken cancellationToken = default);

    /// <summary>Stream results as <see cref="BsonDocument"/> values.</summary>
    IAsyncEnumerable<BsonDocument> ToDocuments(CancellationToken cancellationToken = default);

    /// <summary>Stream results as <typeparamref name="T"/> values.</summary>
    IAsyncEnumerable<T> ToEnumerable(CancellationToken cancellationToken = default);

    /// <summary>Materialise all results into a <see cref="List{T}"/>.</summary>
    ValueTask<List<T>> ToList(CancellationToken cancellationToken = default);

    /// <summary>Materialise all results into an array.</summary>
    ValueTask<T[]> ToArray(CancellationToken cancellationToken = default);

    /// <summary>Insert all results into <paramref name="newCollection"/>. Returns the number of documents inserted.</summary>
    ValueTask<int> Into(string newCollection, BsonAutoId autoId = BsonAutoId.ObjectId, CancellationToken cancellationToken = default);

    /// <summary>Return the first element, throwing if the sequence is empty.</summary>
    ValueTask<T> First(CancellationToken cancellationToken = default);

    /// <summary>Return the first element, or the default for <typeparamref name="T"/> if empty.</summary>
    ValueTask<T> FirstOrDefault(CancellationToken cancellationToken = default);

    /// <summary>Return the single element, throwing if the sequence is empty or has more than one element.</summary>
    ValueTask<T> Single(CancellationToken cancellationToken = default);

    /// <summary>Return the single element, or the default for <typeparamref name="T"/> if empty. Throws if more than one.</summary>
    ValueTask<T> SingleOrDefault(CancellationToken cancellationToken = default);

    /// <summary>Count matching documents.</summary>
    ValueTask<int> Count(CancellationToken cancellationToken = default);

    /// <summary>Count matching documents as a <c>long</c>.</summary>
    ValueTask<long> LongCount(CancellationToken cancellationToken = default);

    /// <summary>Returns <c>true</c> if at least one document matches the query.</summary>
    ValueTask<bool> Exists(CancellationToken cancellationToken = default);
}