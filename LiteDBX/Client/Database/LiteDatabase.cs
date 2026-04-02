using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteDbX.Engine;

namespace LiteDbX;

/// <summary>
/// The LiteDBX database. Used for creating a LiteDBX instance and using all storage resources.
///
/// Phase 4: Updated to implement <see cref="ILiteDatabase"/>.
/// All data operations, transactions, maintenance, and SQL execution are now async-only.
/// The former synchronous ambient-transaction API (BeginTrans/Commit/Rollback) is removed;
/// use <see cref="BeginTransaction"/> and the returned <see cref="ILiteTransaction"/> instead.
///
/// The supported lifecycle is <see cref="Open(string, BsonMapper, CancellationToken)"/>,
/// <see cref="Open(ConnectionString, BsonMapper, CancellationToken)"/>, or
/// <see cref="Open(Stream, BsonMapper, Stream, CancellationToken)"/> together with
/// <c>await using</c>. Constructor-based open and synchronous configuration properties remain as
/// compatibility bridges only.
/// </summary>
public class LiteDatabase : ILiteDatabase, IDisposable
{
    private readonly ILiteEngine _engine;
    private readonly bool _disposeOnClose;
    private ILiteStorage<string> _fs;

    /// <summary>
    /// Open a database from a connection string using the supported async-first lifecycle.
    /// </summary>
    public static ValueTask<LiteDatabase> Open(
        string connectionString,
        BsonMapper mapper = null,
        CancellationToken cancellationToken = default)
        => Open(new ConnectionString(connectionString), mapper, cancellationToken);

    /// <summary>
    /// Open a database from a parsed <see cref="ConnectionString"/> using the supported async-first lifecycle.
    /// </summary>
    public static async ValueTask<LiteDatabase> Open(
        ConnectionString connectionString,
        BsonMapper mapper = null,
        CancellationToken cancellationToken = default)
    {
        if (connectionString == null) throw new ArgumentNullException(nameof(connectionString));

        var engine = await connectionString.OpenEngine(cancellationToken: cancellationToken).ConfigureAwait(false);
        return new LiteDatabase(engine, mapper, disposeOnClose: true);
    }

    /// <summary>
    /// Open a stream-backed database using the supported async-first lifecycle.
    /// </summary>
    public static async ValueTask<LiteDatabase> Open(
        Stream stream,
        BsonMapper mapper = null,
        Stream logStream = null,
        CancellationToken cancellationToken = default)
    {
        var engine = await LiteEngine.Open(new EngineSettings
        {
            DataStream = stream ?? throw new ArgumentNullException(nameof(stream)),
            LogStream = logStream
        }, cancellationToken).ConfigureAwait(false);

        return new LiteDatabase(engine, mapper, disposeOnClose: true);
    }

    #region Constructors

    /// <summary>
    /// Starts LiteDBX database using a connection string for file system database.
    /// Transitional synchronous lifecycle path retained for compatibility; prefer
    /// <see cref="Open(string, BsonMapper, CancellationToken)"/>.
    /// </summary>
    public LiteDatabase(string connectionString, BsonMapper mapper = null)
        : this(new ConnectionString(connectionString), mapper) { }

    /// <summary>
    /// Starts LiteDBX database using a connection string for file system database.
    /// Transitional synchronous lifecycle path retained for compatibility; prefer
    /// <see cref="Open(ConnectionString, BsonMapper, CancellationToken)"/>.
    /// </summary>
    public LiteDatabase(ConnectionString connectionString, BsonMapper mapper = null)
    {
        if (connectionString == null) throw new ArgumentNullException(nameof(connectionString));
        _engine = connectionString.CreateEngine();
        Mapper = mapper ?? BsonMapper.Global;
        _disposeOnClose = true;
    }

    /// <summary>
    /// Starts LiteDBX database using a generic Stream implementation (mostly MemoryStream).
    /// Transitional synchronous lifecycle path retained for compatibility; prefer
    /// <see cref="Open(Stream, BsonMapper, Stream, CancellationToken)"/>.
    /// </summary>
    /// <param name="stream">DataStream reference </param>
    /// <param name="mapper">BsonMapper mapper reference</param>
    /// <param name="logStream">LogStream reference </param>
    public LiteDatabase(Stream stream, BsonMapper mapper = null, Stream logStream = null)
    {
        _engine = new LiteEngine(new EngineSettings
        {
            DataStream = stream ?? throw new ArgumentNullException(nameof(stream)),
            LogStream = logStream
        });
        Mapper = mapper ?? BsonMapper.Global;
        _disposeOnClose = true;
    }

    /// <summary>
    /// Wrap an already-open <see cref="ILiteEngine"/>.
    ///
    /// This overload does not perform any open work itself. Use it when engine ownership is managed
    /// externally and control disposal with <paramref name="disposeOnClose"/>.
    /// </summary>
    public LiteDatabase(ILiteEngine engine, BsonMapper mapper = null, bool disposeOnClose = true)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        Mapper = mapper ?? BsonMapper.Global;
        _disposeOnClose = disposeOnClose;
    }

    #endregion

    // ── ILiteDatabase — Configuration ─────────────────────────────────────────

    /// <summary>
    /// Get current instance of BsonMapper used in this database instance (can be BsonMapper.Global)
    /// </summary>
    public BsonMapper Mapper { get; }

    /// <summary>
    /// Returns a special collection for storage files/stream inside datafile. Use _files and _chunks collection names. FileId
    /// is implemented as string. Use "GetStorage" for custom options
    /// </summary>
    public ILiteStorage<string> FileStorage => _fs ??= GetStorage<string>();

    /// <summary>
    /// Get/Set database user version - use this version number to control database change model.
    /// Transitional synchronous bridge; prefer async <see cref="Pragma(string, CancellationToken)"/>
    /// and <see cref="Pragma(string, BsonValue, CancellationToken)"/> for async-first configuration access.
    /// </summary>
    public int UserVersion
    {
        get => _engine.Pragma(Pragmas.USER_VERSION).GetAwaiter().GetResult();
        set => _engine.Pragma(Pragmas.USER_VERSION, value).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Get/Set database timeout - this timeout is used to wait for unlock using transactions.
    /// Transitional synchronous bridge; prefer async pragma access via <see cref="Pragma(string, CancellationToken)"/>
    /// or <see cref="Pragma(string, BsonValue, CancellationToken)"/>.
    /// </summary>
    public TimeSpan Timeout
    {
        get => TimeSpan.FromSeconds(_engine.Pragma(Pragmas.TIMEOUT).GetAwaiter().GetResult().AsInt32);
        set => _engine.Pragma(Pragmas.TIMEOUT, (int)value.TotalSeconds).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Get/Set if database will deserialize dates in UTC timezone or Local timezone (default: Local).
    /// Transitional synchronous bridge; prefer async pragma access via <see cref="Pragma(string, CancellationToken)"/>
    /// or <see cref="Pragma(string, BsonValue, CancellationToken)"/>.
    /// </summary>
    public bool UtcDate
    {
        get => _engine.Pragma(Pragmas.UTC_DATE).GetAwaiter().GetResult().AsBoolean;
        set => _engine.Pragma(Pragmas.UTC_DATE, value).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Get/Set database limit size (in bytes). New value must be equals or larger than current database size.
    /// Transitional synchronous bridge; prefer async pragma access via <see cref="Pragma(string, CancellationToken)"/>
    /// or <see cref="Pragma(string, BsonValue, CancellationToken)"/>.
    /// </summary>
    public long LimitSize
    {
        get => _engine.Pragma(Pragmas.LIMIT_SIZE).GetAwaiter().GetResult().AsInt64;
        set => _engine.Pragma(Pragmas.LIMIT_SIZE, value).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Get/Set in how many pages (8 Kb each page) log file will auto checkpoint (copy from log file to data file). Use 0 to
    /// manual-only checkpoint (and no checkpoint on dispose)
    /// Default: 1000 pages.
    /// Transitional synchronous bridge; prefer async pragma access via <see cref="Pragma(string, CancellationToken)"/>
    /// or <see cref="Pragma(string, BsonValue, CancellationToken)"/>.
    /// </summary>
    public int CheckpointSize
    {
        get => _engine.Pragma(Pragmas.CHECKPOINT).GetAwaiter().GetResult().AsInt32;
        set => _engine.Pragma(Pragmas.CHECKPOINT, value).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Get database collection (this options can be changed only in rebuild proces).
    /// Transitional synchronous bridge; prefer async pragma access via <see cref="Pragma(string, CancellationToken)"/>.
    /// </summary>
    public Collation Collation
        => new(_engine.Pragma(Pragmas.COLLATION).GetAwaiter().GetResult().AsString);

    // ── ILiteDatabase — Collection factory (sync — no I/O) ───────────────────

    /// <summary>
    /// Get a collection using an entity class as strong typed document. If collection does not exist, create a new one.
    /// </summary>
    /// <param name="name">Collection name (case insensitive)</param>
    /// <param name="autoId">Define autoId data type (when object contains no id field)</param>
    public ILiteCollection<T> GetCollection<T>(string name, BsonAutoId autoId = BsonAutoId.ObjectId)
        => new LiteCollection<T>(name, autoId, _engine, Mapper);

    /// <summary>
    /// Get a collection using a name based on typeof(T).Name (BsonMapper.ResolveCollectionName function)
    /// </summary>
    public ILiteCollection<T> GetCollection<T>()
        => GetCollection<T>(null);

    /// <summary>
    /// Get a collection using a name based on typeof(T).Name (BsonMapper.ResolveCollectionName function)
    /// </summary>
    public ILiteCollection<T> GetCollection<T>(BsonAutoId autoId)
        => GetCollection<T>(null, autoId);

    /// <summary>
    /// Get a collection using a generic BsonDocument. If collection does not exist, create a new one.
    /// </summary>
    /// <param name="name">Collection name (case insensitive)</param>
    /// <param name="autoId">Define autoId data type (when document contains no _id field)</param>
    public ILiteCollection<BsonDocument> GetCollection(string name, BsonAutoId autoId = BsonAutoId.ObjectId)
    {
        if (name.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(name));
        return new LiteCollection<BsonDocument>(name, autoId, _engine, Mapper);
    }

    // ── ILiteDatabase — File storage ──────────────────────────────────────────

    /// <summary>
    /// Get new instance of Storage using custom FileId type, custom "_files" collection name and custom "_chunks" collection.
    /// LiteDBX support multiples file storages (using different files/chunks collection names)
    /// </summary>
    public ILiteStorage<TFileId> GetStorage<TFileId>(string filesCollection = "_files", string chunksCollection = "_chunks")
        => new LiteStorage<TFileId>(this, filesCollection, chunksCollection);

    // ── ILiteDatabase — Transactions ──────────────────────────────────────────

    /// <summary>
    /// Initialize a new transaction. Transaction are created "per-thread". There is only one single transaction per thread.
    /// Return true if transaction was created or false if current thread already in a transaction.
    /// </summary>
    public ValueTask<ILiteTransaction> BeginTransaction(CancellationToken cancellationToken = default)
        => _engine.BeginTransaction(cancellationToken);

    // ── ILiteDatabase — Schema management ────────────────────────────────────

    /// <summary>
    /// Get all collections name inside this database.
    /// </summary>
    public async IAsyncEnumerable<string> GetCollectionNames(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var col = GetCollection("$cols");
        await foreach (var doc in col.Query().Where("type = 'user'").ToDocuments(cancellationToken).ConfigureAwait(false))
        {
            yield return doc["name"].AsString;
        }
    }

    /// <summary>
    /// Checks if a collection exists on database. Collection name is case insensitive
    /// </summary>
    public async ValueTask<bool> CollectionExists(string name, CancellationToken cancellationToken = default)
    {
        if (name.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(name));

        await foreach (var colName in GetCollectionNames(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(colName, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Drop a collection and all data + indexes
    /// </summary>
    public ValueTask<bool> DropCollection(string name, CancellationToken cancellationToken = default)
    {
        if (name.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(name));
        return _engine.DropCollection(name, cancellationToken);
    }

    /// <summary>
    /// Rename a collection. Returns false if oldName does not exists or newName already exists
    /// </summary>
    public ValueTask<bool> RenameCollection(string oldName, string newName, CancellationToken cancellationToken = default)
    {
        if (oldName.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(oldName));
        if (newName.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(newName));
        return _engine.RenameCollection(oldName, newName, cancellationToken);
    }

    // ── ILiteDatabase — SQL execution ─────────────────────────────────────────

    /// <summary>
    /// Execute SQL commands and return as data reader.
    /// </summary>
    public async ValueTask<IBsonDataReader> Execute(
        TextReader commandReader,
        BsonDocument parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (commandReader == null) throw new ArgumentNullException(nameof(commandReader));

        var collation = new Collation((await _engine.Pragma(Pragmas.COLLATION, cancellationToken).ConfigureAwait(false)).AsString);
        var tokenizer = new Tokenizer(commandReader);
        var sql = new SqlParser(_engine, tokenizer, parameters, collation);
        return await sql.Execute(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Execute SQL commands and return as data reader
    /// </summary>
    public async ValueTask<IBsonDataReader> Execute(
        string command,
        BsonDocument parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));

        var collation = new Collation((await _engine.Pragma(Pragmas.COLLATION, cancellationToken).ConfigureAwait(false)).AsString);
        var tokenizer = new Tokenizer(command);
        var sql = new SqlParser(_engine, tokenizer, parameters, collation);
        return await sql.Execute(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Execute SQL commands and return as data reader
    /// </summary>
    public async ValueTask<IBsonDataReader> Execute(string command, params BsonValue[] args)
    {
        var p = new BsonDocument();
        var index = 0;
        foreach (var arg in args) p[index++.ToString()] = arg;
        return await Execute(command, p).ConfigureAwait(false);
    }

    // ── ILiteDatabase — Maintenance ───────────────────────────────────────────

    /// <summary>
    /// Do database checkpoint. Copy all commited transaction from log file into datafile.
    /// </summary>
    public async ValueTask Checkpoint(CancellationToken cancellationToken = default)
    {
        await _engine.Checkpoint(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rebuild all database to remove unused pages - reduce data file
    /// </summary>
    public ValueTask<long> Rebuild(RebuildOptions options = null, CancellationToken cancellationToken = default)
        => _engine.Rebuild(options ?? new RebuildOptions(), cancellationToken);

    // ── ILiteDatabase — Pragmas ───────────────────────────────────────────────

    /// <summary>
    /// Get value from internal engine variables
    /// </summary>
    public ValueTask<BsonValue> Pragma(string name, CancellationToken cancellationToken = default)
        => _engine.Pragma(name, cancellationToken);

    /// <summary>
    /// Set new value to internal engine variables
    /// </summary>
    public async ValueTask<BsonValue> Pragma(string name, BsonValue value, CancellationToken cancellationToken = default)
    {
        var old = await _engine.Pragma(name, cancellationToken).ConfigureAwait(false);
        await _engine.Pragma(name, value, cancellationToken).ConfigureAwait(false);
        return old;
    }

    // ── IAsyncDisposable / IDisposable ────────────────────────────────────────

    /// <summary>
    /// Dispose the database using the async-first lifecycle. Prefer <c>await using</c> so engine
    /// shutdown, checkpoint, and stream cleanup can complete without blocking a thread.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposeOnClose)
        {
            await _engine.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Blocking disposal convenience retained for compatibility. Prefer <see cref="DisposeAsync"/>
    /// or <c>await using</c> in async-first code.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~LiteDatabase() { Dispose(false); }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing && _disposeOnClose && _engine is IDisposable d)
        {
            d.Dispose();
        }
    }
}