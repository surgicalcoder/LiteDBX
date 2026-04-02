using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LiteDbX.Engine;

namespace LiteDbX;

/// <summary>
/// Async-only public database contract.
///
/// All data operations, maintenance, and schema management are async.
/// The former ambient per-thread transaction model (<c>BeginTrans</c>, <c>Commit</c>, <c>Rollback</c>)
/// has been replaced with explicit <see cref="ILiteTransaction"/> scope objects via
/// <see cref="BeginTransaction"/>.
///
/// Lifecycle:
/// - Open/create the database with the concrete type's static <c>Open(...)</c> factory.
/// - Use <c>await using</c> for deterministic async disposal.
///
/// The configuration properties below are retained as transitional synchronous bridges over pragma access.
/// Async-first callers should prefer <see cref="Pragma(string, CancellationToken)"/> and
/// <see cref="Pragma(string, BsonValue, CancellationToken)"/>.
/// </summary>
public interface ILiteDatabase : IAsyncDisposable
{
    // ── Configuration ─────────────────────────────────────────────────────────

    /// <summary>The <see cref="BsonMapper"/> instance used by this database.</summary>
    BsonMapper Mapper { get; }

    /// <summary>
    /// Default file storage using <c>_files</c> and <c>_chunks</c> collection names with <c>string</c> file IDs.
    /// </summary>
    ILiteStorage<string> FileStorage { get; }

    /// <summary>User-defined database schema version number. Transitional synchronous bridge over pragma access.</summary>
    int UserVersion { get; set; }

    /// <summary>Timeout used when waiting for lock acquisition. Transitional synchronous bridge over pragma access.</summary>
    TimeSpan Timeout { get; set; }

    /// <summary>When <c>true</c>, dates are returned in UTC; otherwise local time. Transitional synchronous bridge over pragma access.</summary>
    bool UtcDate { get; set; }

    /// <summary>Maximum allowed data file size in bytes. Transitional synchronous bridge over pragma access.</summary>
    long LimitSize { get; set; }

    /// <summary>
    /// Number of WAL pages that trigger an auto-checkpoint. Use 0 for manual-only checkpointing.
    /// Default: 1000.
    /// Transitional synchronous bridge over pragma access.
    /// </summary>
    int CheckpointSize { get; set; }

    /// <summary>Read-only collation used by this database (changeable only via rebuild). Transitional synchronous bridge over pragma access.</summary>
    Collation Collation { get; }

    // ── Collection access (sync factory — returns a handle, no I/O occurs here) ──

    /// <summary>Get or create a typed collection by explicit name.</summary>
    ILiteCollection<T> GetCollection<T>(string name, BsonAutoId autoId = BsonAutoId.ObjectId);

    /// <summary>Get or create a typed collection whose name is derived from <typeparamref name="T"/>.</summary>
    ILiteCollection<T> GetCollection<T>();

    /// <summary>Get or create a typed collection whose name is derived from <typeparamref name="T"/>.</summary>
    ILiteCollection<T> GetCollection<T>(BsonAutoId autoId);

    /// <summary>Get or create an untyped <see cref="BsonDocument"/> collection by name.</summary>
    ILiteCollection<BsonDocument> GetCollection(string name, BsonAutoId autoId = BsonAutoId.ObjectId);

    // ── File storage (sync factory — no I/O at handle creation time) ──────────

    /// <summary>
    /// Get a file storage instance using custom collection names and a custom file ID type.
    /// </summary>
    ILiteStorage<TFileId> GetStorage<TFileId>(string filesCollection = "_files", string chunksCollection = "_chunks");

    // ── Transactions ─────────────────────────────────────────────────────────

    /// <summary>
    /// Begin an explicit async transaction scope.
    /// Dispose the returned <see cref="ILiteTransaction"/> without committing to trigger a rollback.
    /// </summary>
    ValueTask<ILiteTransaction> BeginTransaction(CancellationToken cancellationToken = default);

    // ── Schema management ─────────────────────────────────────────────────────

    /// <summary>Stream all collection names in this database.</summary>
    IAsyncEnumerable<string> GetCollectionNames(CancellationToken cancellationToken = default);

    /// <summary>Returns <c>true</c> if a collection with the given name exists.</summary>
    ValueTask<bool> CollectionExists(string name, CancellationToken cancellationToken = default);

    /// <summary>Drop a collection and all its data and indexes. Returns <c>true</c> if it existed.</summary>
    ValueTask<bool> DropCollection(string name, CancellationToken cancellationToken = default);

    /// <summary>Rename a collection. Returns <c>false</c> if <paramref name="oldName"/> does not exist or <paramref name="newName"/> is already taken.</summary>
    ValueTask<bool> RenameCollection(string oldName, string newName, CancellationToken cancellationToken = default);

    // ── SQL execution ─────────────────────────────────────────────────────────

    /// <summary>
    /// Execute one or more SQL commands supplied via a <see cref="TextReader"/> and return an async cursor.
    /// The cursor carries collection context and supports field-level access.
    /// </summary>
    ValueTask<IBsonDataReader> Execute(TextReader commandReader, BsonDocument parameters = null, CancellationToken cancellationToken = default);

    /// <summary>Execute a SQL command string and return an async cursor.</summary>
    ValueTask<IBsonDataReader> Execute(string command, BsonDocument parameters = null, CancellationToken cancellationToken = default);

    /// <summary>Execute a SQL command string with positional parameters and return an async cursor.</summary>
    ValueTask<IBsonDataReader> Execute(string command, params BsonValue[] args);

    // ── Maintenance ───────────────────────────────────────────────────────────

    /// <summary>
    /// Flush all committed WAL pages to the main data file.
    /// </summary>
    ValueTask Checkpoint(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuild the database file, reclaiming unused pages.
    /// Returns the new size of the data file in bytes.
    /// </summary>
    ValueTask<long> Rebuild(RebuildOptions options = null, CancellationToken cancellationToken = default);

    // ── Pragmas ───────────────────────────────────────────────────────────────

    /// <summary>Read an internal engine pragma value.</summary>
    ValueTask<BsonValue> Pragma(string name, CancellationToken cancellationToken = default);

    /// <summary>Write an internal engine pragma value. Returns the old value.</summary>
    ValueTask<BsonValue> Pragma(string name, BsonValue value, CancellationToken cancellationToken = default);
}