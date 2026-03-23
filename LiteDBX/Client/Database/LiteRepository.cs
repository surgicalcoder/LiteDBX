using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

/// <summary>
/// Async-only repository convenience wrapper around <see cref="ILiteDatabase"/>.
/// Implements <see cref="ILiteRepository"/> — all storage operations are async.
/// </summary>
public class LiteRepository : ILiteRepository
{
    #region Properties

    /// <inheritdoc/>
    public ILiteDatabase Database { get; }

    #endregion

    #region Constructors

    /// <summary>Wrap an existing <see cref="ILiteDatabase"/> instance.</summary>
    public LiteRepository(ILiteDatabase database)
    {
        Database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <summary>Open a database from a connection string.</summary>
    public LiteRepository(string connectionString, BsonMapper mapper = null)
    {
        Database = new LiteDatabase(connectionString, mapper);
    }

    /// <summary>Open a database from a <see cref="ConnectionString"/>.</summary>
    public LiteRepository(ConnectionString connectionString, BsonMapper mapper = null)
    {
        Database = new LiteDatabase(connectionString, mapper);
    }

    /// <summary>Open an in-memory or stream-backed database.</summary>
    public LiteRepository(Stream stream, BsonMapper mapper = null, Stream logStream = null)
    {
        Database = new LiteDatabase(stream, mapper, logStream);
    }

    #endregion

    #region Query (sync builder — no I/O)

    /// <inheritdoc/>
    public ILiteQueryable<T> Query<T>(string collectionName = null)
        => Database.GetCollection<T>(collectionName).Query();

    #endregion

    #region Insert

    /// <inheritdoc/>
    public ValueTask<BsonValue> Insert<T>(T entity, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).Insert(entity, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<int> Insert<T>(IEnumerable<T> entities, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).Insert(entities, cancellationToken);

    #endregion

    #region Update

    /// <inheritdoc/>
    public ValueTask<bool> Update<T>(T entity, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).Update(entity, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<int> Update<T>(IEnumerable<T> entities, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).Update(entities, cancellationToken);

    #endregion

    #region Upsert

    /// <inheritdoc/>
    public ValueTask<bool> Upsert<T>(T entity, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).Upsert(entity, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<int> Upsert<T>(IEnumerable<T> entities, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).Upsert(entities, cancellationToken);

    #endregion

    #region Delete

    /// <inheritdoc/>
    public ValueTask<bool> Delete<T>(BsonValue id, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).Delete(id, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<int> DeleteMany<T>(BsonExpression predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).DeleteMany(predicate, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<int> DeleteMany<T>(Expression<Func<T, bool>> predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).DeleteMany(predicate, cancellationToken);

    #endregion

    #region EnsureIndex

    /// <inheritdoc/>
    public ValueTask<bool> EnsureIndex<T>(string name, BsonExpression expression, bool unique = false, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).EnsureIndex(name, expression, unique, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<bool> EnsureIndex<T>(BsonExpression expression, bool unique = false, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).EnsureIndex(expression, unique, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<bool> EnsureIndex<T, K>(Expression<Func<T, K>> keySelector, bool unique = false, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).EnsureIndex(keySelector, unique, cancellationToken);

    /// <inheritdoc/>
    public ValueTask<bool> EnsureIndex<T, K>(string name, Expression<Func<T, K>> keySelector, bool unique = false, string collectionName = null, CancellationToken cancellationToken = default)
        => Database.GetCollection<T>(collectionName).EnsureIndex(name, keySelector, unique, cancellationToken);

    #endregion

    #region Convenience queries

    /// <inheritdoc/>
    public ValueTask<T> SingleById<T>(BsonValue id, string collectionName = null, CancellationToken cancellationToken = default)
        => Query<T>(collectionName).Where("_id = @0", id).Single(cancellationToken);

    /// <inheritdoc/>
    public ValueTask<List<T>> Fetch<T>(BsonExpression predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Query<T>(collectionName).Where(predicate).ToList(cancellationToken);

    /// <inheritdoc/>
    public ValueTask<List<T>> Fetch<T>(Expression<Func<T, bool>> predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Query<T>(collectionName).Where(predicate).ToList(cancellationToken);

    /// <inheritdoc/>
    public ValueTask<T> First<T>(BsonExpression predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Query<T>(collectionName).Where(predicate).First(cancellationToken);

    /// <inheritdoc/>
    public ValueTask<T> First<T>(Expression<Func<T, bool>> predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Query<T>(collectionName).Where(predicate).First(cancellationToken);

    /// <inheritdoc/>
    public ValueTask<T> FirstOrDefault<T>(BsonExpression predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Query<T>(collectionName).Where(predicate).FirstOrDefault(cancellationToken);

    /// <inheritdoc/>
    public ValueTask<T> FirstOrDefault<T>(Expression<Func<T, bool>> predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Query<T>(collectionName).Where(predicate).FirstOrDefault(cancellationToken);

    /// <inheritdoc/>
    public ValueTask<T> Single<T>(BsonExpression predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Query<T>(collectionName).Where(predicate).Single(cancellationToken);

    /// <inheritdoc/>
    public ValueTask<T> Single<T>(Expression<Func<T, bool>> predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Query<T>(collectionName).Where(predicate).Single(cancellationToken);

    /// <inheritdoc/>
    public ValueTask<T> SingleOrDefault<T>(BsonExpression predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Query<T>(collectionName).Where(predicate).SingleOrDefault(cancellationToken);

    /// <inheritdoc/>
    public ValueTask<T> SingleOrDefault<T>(Expression<Func<T, bool>> predicate, string collectionName = null, CancellationToken cancellationToken = default)
        => Query<T>(collectionName).Where(predicate).SingleOrDefault(cancellationToken);

    #endregion

    #region Lifecycle

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => Database.DisposeAsync();

    /// <summary>
    /// Synchronous dispose convenience. Delegates to <see cref="DisposeAsync"/> and blocks.
    /// Prefer <c>await using</c> where possible.
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    ~LiteRepository()
    {
        Dispose();
    }

    #endregion
}