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
/// Prefer <see cref="Open(string, BsonMapper, CancellationToken)"/>,
/// <see cref="Open(ConnectionString, BsonMapper, CancellationToken)"/>, or
/// <see cref="Open(Stream, BsonMapper, Stream, CancellationToken)"/> together with
/// <c>await using</c>. Constructor-based opens and blocking <see cref="Dispose()"/> remain as
/// compatibility bridges only.
/// </summary>
public class LiteRepository : ILiteRepository
{
    #region Properties

    /// <inheritdoc/>
    public ILiteDatabase Database { get; }

    #endregion

    #region Open

    /// <summary>Open a repository from a connection string using the explicit async-first lifecycle.</summary>
    public static async ValueTask<LiteRepository> Open(
        string connectionString,
        BsonMapper mapper = null,
        CancellationToken cancellationToken = default)
        => new(await LiteDatabase.Open(connectionString, mapper, cancellationToken).ConfigureAwait(false));

    /// <summary>Open a repository from a <see cref="ConnectionString"/> using the explicit async-first lifecycle.</summary>
    public static async ValueTask<LiteRepository> Open(
        ConnectionString connectionString,
        BsonMapper mapper = null,
        CancellationToken cancellationToken = default)
        => new(await LiteDatabase.Open(connectionString, mapper, cancellationToken).ConfigureAwait(false));

    /// <summary>Open a stream-backed repository using the explicit async-first lifecycle.</summary>
    public static async ValueTask<LiteRepository> Open(
        Stream stream,
        BsonMapper mapper = null,
        Stream logStream = null,
        CancellationToken cancellationToken = default)
        => new(await LiteDatabase.Open(stream, mapper, logStream, cancellationToken).ConfigureAwait(false));

    #endregion

    #region Constructors

    /// <summary>
    /// Wrap an existing <see cref="ILiteDatabase"/> instance.
    /// This overload does not open a database; it only layers repository helpers over an already
    /// initialized database instance.
    /// </summary>
    public LiteRepository(ILiteDatabase database)
    {
        Database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <summary>
    /// Transitional synchronous lifecycle path retained for compatibility; prefer
    /// <see cref="Open(string, BsonMapper, CancellationToken)"/>.
    /// </summary>
    public LiteRepository(string connectionString, BsonMapper mapper = null)
    {
        Database = new LiteDatabase(connectionString, mapper);
    }

    /// <summary>
    /// Transitional synchronous lifecycle path retained for compatibility; prefer
    /// <see cref="Open(ConnectionString, BsonMapper, CancellationToken)"/>.
    /// </summary>
    public LiteRepository(ConnectionString connectionString, BsonMapper mapper = null)
    {
        Database = new LiteDatabase(connectionString, mapper);
    }

    /// <summary>
    /// Transitional synchronous lifecycle path retained for compatibility; prefer
    /// <see cref="Open(Stream, BsonMapper, Stream, CancellationToken)"/>.
    /// </summary>
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
    {
        var collection = (LiteCollection<T>)Database.GetCollection<T>(collectionName);
        var normalizedId = collection.NormalizeId(id);

        return collection.Query().Where("_id = @0", new[] { normalizedId }).Single(cancellationToken);
    }

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
    /// Synchronous dispose convenience retained for compatibility. Delegates to
    /// <see cref="DisposeAsync"/> and blocks; prefer <c>await using</c> where possible.
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