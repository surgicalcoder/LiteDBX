using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Engine;

/// <summary>
/// Async-only engine contract.
///
/// All data access, maintenance, and schema operations are async.
/// Transactions are represented as explicit <see cref="ILiteTransaction"/> scope objects rather
/// than ambient per-thread state; this removes the thread-affinity requirement that is
/// incompatible with <c>await</c>-based continuations.
///
/// Lifecycle is provided by the concrete <see cref="LiteEngine"/> type's static <c>Open(...)</c>
/// factory, followed by <c>await using</c> / <see cref="IAsyncDisposable.DisposeAsync"/>.
/// </summary>
public interface ILiteEngine : IAsyncDisposable
{
    // ── Maintenance ───────────────────────────────────────────────────────────

    /// <summary>
    /// Copy all committed log pages into the main data file (WAL checkpoint).
    /// Returns the number of pages written.
    /// </summary>
    ValueTask<int> Checkpoint(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuild the entire database, reclaiming unused pages and optionally re-encrypting.
    /// Returns the new file size in bytes.
    /// </summary>
    ValueTask<long> Rebuild(RebuildOptions options, CancellationToken cancellationToken = default);

    // ── Transactions ─────────────────────────────────────────────────────────

    /// <summary>
    /// Begin an explicit async transaction.
    /// The returned <see cref="ILiteTransaction"/> must be committed or rolled back before disposal.
    /// Disposing without committing triggers an implicit rollback.
    /// </summary>
    ValueTask<ILiteTransaction> BeginTransaction(CancellationToken cancellationToken = default);

    // ── Queries ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Execute a query against <paramref name="collection"/> and stream matching documents.
    /// </summary>
    IAsyncEnumerable<BsonDocument> Query(string collection, Query query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a query against <paramref name="collection"/> using the provided explicit transaction.
    /// </summary>
    IAsyncEnumerable<BsonDocument> Query(string collection, Query query, ILiteTransaction transaction, CancellationToken cancellationToken = default);

    // ── Write operations ──────────────────────────────────────────────────────

    /// <summary>Insert one or more documents. Returns the number of documents inserted.</summary>
    ValueTask<int> Insert(string collection, IEnumerable<BsonDocument> docs, BsonAutoId autoId, CancellationToken cancellationToken = default);

    /// <summary>Insert one or more documents using the provided explicit transaction. Returns the number of documents inserted.</summary>
    ValueTask<int> Insert(string collection, IEnumerable<BsonDocument> docs, BsonAutoId autoId, ILiteTransaction transaction, CancellationToken cancellationToken = default);

    /// <summary>Update a set of documents by <c>_id</c>. Returns the number of documents updated.</summary>
    ValueTask<int> Update(string collection, IEnumerable<BsonDocument> docs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply a transform expression to all documents matching <paramref name="predicate"/>.
    /// Returns the number of documents updated.
    /// </summary>
    ValueTask<int> UpdateMany(string collection, BsonExpression transform, BsonExpression predicate, CancellationToken cancellationToken = default);

    /// <summary>Insert or update documents by <c>_id</c>. Returns the number of documents inserted or updated.</summary>
    ValueTask<int> Upsert(string collection, IEnumerable<BsonDocument> docs, BsonAutoId autoId, CancellationToken cancellationToken = default);

    /// <summary>Delete documents by <c>_id</c>. Returns the number of documents deleted.</summary>
    ValueTask<int> Delete(string collection, IEnumerable<BsonValue> ids, CancellationToken cancellationToken = default);

    /// <summary>Delete all documents matching <paramref name="predicate"/>. Returns the number deleted.</summary>
    ValueTask<int> DeleteMany(string collection, BsonExpression predicate, CancellationToken cancellationToken = default);

    // ── Schema management ─────────────────────────────────────────────────────

    /// <summary>Drop a collection and all its data and indexes. Returns <c>true</c> if the collection existed.</summary>
    ValueTask<bool> DropCollection(string name, CancellationToken cancellationToken = default);

    /// <summary>Rename a collection. Returns <c>true</c> if rename was successful.</summary>
    ValueTask<bool> RenameCollection(string name, string newName, CancellationToken cancellationToken = default);

    // ── Index management ──────────────────────────────────────────────────────

    /// <summary>
    /// Create a permanent index if it does not already exist.
    /// Returns <c>true</c> if a new index was created, <c>false</c> if it already existed.
    /// </summary>
    ValueTask<bool> EnsureIndex(string collection, string name, BsonExpression expression, bool unique, CancellationToken cancellationToken = default);

    /// <summary>Drop an index. Returns <c>true</c> if the index existed and was removed.</summary>
    ValueTask<bool> DropIndex(string collection, string name, CancellationToken cancellationToken = default);

    // ── Pragmas ───────────────────────────────────────────────────────────────

    /// <summary>Read an internal engine pragma value by name.</summary>
    ValueTask<BsonValue> Pragma(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Write an internal engine pragma value.
    /// Returns <c>true</c> if the value was accepted.
    /// </summary>
    ValueTask<bool> Pragma(string name, BsonValue value, CancellationToken cancellationToken = default);
}