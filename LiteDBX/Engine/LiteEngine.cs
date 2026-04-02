using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteDbX.Utils;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

/// <summary>
/// A public class that take care of all engine data structure access - it´s basic implementation of a NoSql database
/// Its isolated from complete solution - works on low level only (no linq, no poco... just BSON objects)
/// [ThreadSafe]
/// </summary>
public partial class LiteEngine : ILiteEngine, IDisposable
{
    /// <summary>
    /// Explicit async-first engine open boundary.
    ///
    /// Recovery, upgrade detection, rebuild reopen, and WAL restore now run on this awaitable
    /// startup path. The constructor-based startup path is retained only as a transitional
    /// compatibility bridge.
    /// </summary>
    public static async ValueTask<LiteEngine> Open(EngineSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var engine = new LiteEngine(settings, openOnConstruction: false);
        await engine.OpenInstance(cancellationToken).ConfigureAwait(false);
        return engine;
    }

    /// <summary>
    /// Centralized synchronous engine-open compatibility bridge.
    /// Internal callers that still need constructor-era sync startup should go through this helper
    /// instead of constructing <see cref="LiteEngine"/> ad hoc.
    /// </summary>
    internal static LiteEngine OpenSync(EngineSettings settings)
    {
        var engine = new LiteEngine(settings, openOnConstruction: false);
        engine.Open();
        return engine;
    }

    /// <summary>
    /// Run checkpoint command to copy log file into data file.
    /// Phase 3: genuinely async — delegates to <see cref="WalIndexService.Checkpoint"/> which
    /// uses <see cref="DiskService.ReadFullAsync"/> and <see cref="DiskService.WriteDataDisk"/>.
    /// </summary>
    public ValueTask<int> Checkpoint(CancellationToken cancellationToken = default)
    {
        return _walIndex.Checkpoint(cancellationToken);
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    /// <summary>
    /// Async dispose — satisfies <see cref="ILiteEngine"/> / <see cref="IAsyncDisposable"/> contract.
    /// Phase 3: delegates to the async close helpers so disk I/O on shutdown does not block a thread.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    // ── IDisposable (retained as a sync convenience for internal callers) ──────

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        Close();
    }

    #region Services instances

    private LockService _locker;

    private DiskService _disk;

    private WalIndexService _walIndex;

    private HeaderPage _header;

    private TransactionMonitor _monitor;

    private SortDisk _sortDisk;

    private EngineState _state;

    // immutable settings
    private readonly EngineSettings _settings;

    /// <summary>
    /// All system read-only collections for get metadata database information
    /// </summary>
    private Dictionary<string, SystemCollection> _systemCollections;

    /// <summary>
    /// Sequence cache for collections last ID (for int/long numbers only)
    /// </summary>
    private ConcurrentDictionary<string, long> _sequences;

    #endregion

    #region Ctor

    /// <summary>
    /// Initialize LiteEngine using connection memory database
    /// </summary>
    public LiteEngine()
        : this(new EngineSettings { DataStream = new MemoryStream() }, openOnConstruction: true) { }

    /// <summary>
    /// Initialize LiteEngine using connection string using key=value; parser
    /// </summary>
    public LiteEngine(string filename)
        : this(new EngineSettings { Filename = filename }, openOnConstruction: true) { }

    /// <summary>
    /// Initialize LiteEngine using initial engine settings.
    /// Transitional synchronous lifecycle path retained for compatibility; prefer
    /// <see cref="Open(EngineSettings, CancellationToken)"/> for supported async-first startup.
    /// </summary>
    public LiteEngine(EngineSettings settings)
        : this(settings, openOnConstruction: true)
    {
    }

    private LiteEngine(EngineSettings settings, bool openOnConstruction)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        if (openOnConstruction)
        {
            Open();
        }
    }

    #endregion

    #region Open & Close

    private async ValueTask OpenInstance(CancellationToken cancellationToken)
    {
        LOG($"start initializing{(_settings.ReadOnly ? " (readonly)" : string.Empty)}", "ENGINE");

        _systemCollections = new Dictionary<string, SystemCollection>(StringComparer.OrdinalIgnoreCase);
        _sequences = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // initialize engine state
            _state = new EngineState(this, _settings);

            // before initialize, try if must be upgrade
            if (_settings.Upgrade)
            {
                await TryUpgrade(cancellationToken).ConfigureAwait(false);
            }

            // initialize disk service (will create database if needed)
            _disk = new DiskService(_settings, _state, MEMORY_SEGMENT_SIZES);

            // read page with no cache ref (has a own PageBuffer) - do not Release() support
            var buffer = await ReadFirstPageAsync(FileOrigin.Data, cancellationToken).ConfigureAwait(false);

            // if first byte are 1 this datafile are encrypted but has do defined password to open
            if (buffer[0] == 1)
            {
                throw new LiteException(0, "This data file is encrypted and needs a password to open");
            }

            // read header database page
            _header = new HeaderPage(buffer);

            // if database is set to invalid state, need rebuild
            if (buffer[HeaderPage.P_INVALID_DATAFILE_STATE] != 0 && _settings.AutoRebuild)
            {
                // dispose disk access to rebuild process
                await _disk.DisposeAsync().ConfigureAwait(false);
                _disk = null;

                // rebuild database, create -backup file and include _rebuild_errors collection
                await Recovery(_header.Pragmas.Collation, cancellationToken).ConfigureAwait(false);

                // re-initialize disk service
                _disk = new DiskService(_settings, _state, MEMORY_SEGMENT_SIZES);

                // read buffer header page again
                buffer = await ReadFirstPageAsync(FileOrigin.Data, cancellationToken).ConfigureAwait(false);

                _header = new HeaderPage(buffer);
            }

            // test for same collation
            if (_settings.Collation != null && _settings.Collation.ToString() != _header.Pragmas.Collation.ToString())
            {
                throw new LiteException(0, $"Datafile collation '{_header.Pragmas.Collation}' is different from engine settings. Use Rebuild database to change collation.");
            }

            // initialize locker service
            _locker = new LockService(_header.Pragmas);

            // initialize wal-index service
            _walIndex = new WalIndexService(_disk, _locker);

            // if exists log file, restore wal index references (can update full _header instance)
            if (_disk.GetFileLength(FileOrigin.Log) > 0)
            {
                _header = await _walIndex.RestoreIndex(_header, cancellationToken).ConfigureAwait(false);
            }

            // initialize sort temp disk
            _sortDisk = new SortDisk(_settings.CreateTempFactory(), CONTAINER_SORT_SIZE, _header.Pragmas);

            // initialize transaction monitor as last service
            _monitor = new TransactionMonitor(_header, _locker, _disk, _walIndex);

            // register system collections
            InitializeSystemCollections();

            LOG("initialization completed", "ENGINE");
        }
        catch (Exception ex)
        {
            LOG(ex.Message, "ERROR");

            await CloseAsync(ex, cancellationToken).ConfigureAwait(false);

            throw;
        }
    }

    private async ValueTask<PageBuffer> ReadFirstPageAsync(FileOrigin origin, CancellationToken cancellationToken)
    {
        await foreach (var buffer in _disk.ReadFullAsync(origin, cancellationToken).ConfigureAwait(false))
        {
            return buffer;
        }

        throw LiteException.InvalidDatabase();
    }

    internal bool Open()
    {
        LOG($"start initializing{(_settings.ReadOnly ? " (readonly)" : "")}", "ENGINE");

        _systemCollections = new Dictionary<string, SystemCollection>(StringComparer.OrdinalIgnoreCase);
        _sequences = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // initialize engine state 
            _state = new EngineState(this, _settings);

            // before initilize, try if must be upgrade
            if (_settings.Upgrade)
            {
                TryUpgrade();
            }

            // initialize disk service (will create database if needed)
            _disk = new DiskService(_settings, _state, MEMORY_SEGMENT_SIZES);

            // read page with no cache ref (has a own PageBuffer) - do not Release() support
            var buffer = _disk.ReadFull(FileOrigin.Data).First();

            // if first byte are 1 this datafile are encrypted but has do defined password to open
            if (buffer[0] == 1)
            {
                throw new LiteException(0, "This data file is encrypted and needs a password to open");
            }

            // read header database page
            _header = new HeaderPage(buffer);

            // if database is set to invalid state, need rebuild
            if (buffer[HeaderPage.P_INVALID_DATAFILE_STATE] != 0 && _settings.AutoRebuild)
            {
                // dispose disk access to rebuild process
                _disk.Dispose();
                _disk = null;

                // rebuild database, create -backup file and include _rebuild_errors collection
                Recovery(_header.Pragmas.Collation);

                // re-initialize disk service
                _disk = new DiskService(_settings, _state, MEMORY_SEGMENT_SIZES);

                // read buffer header page again
                buffer = _disk.ReadFull(FileOrigin.Data).First();

                _header = new HeaderPage(buffer);
            }

            // test for same collation
            if (_settings.Collation != null && _settings.Collation.ToString() != _header.Pragmas.Collation.ToString())
            {
                throw new LiteException(0, $"Datafile collation '{_header.Pragmas.Collation}' is different from engine settings. Use Rebuild database to change collation.");
            }

            // initialize locker service
            _locker = new LockService(_header.Pragmas);

            // initialize wal-index service
            _walIndex = new WalIndexService(_disk, _locker);

            // if exists log file, restore wal index references (can update full _header instance)
            if (_disk.GetFileLength(FileOrigin.Log) > 0)
            {
                _walIndex.RestoreIndex(ref _header);
            }

            // initialize sort temp disk
            _sortDisk = new SortDisk(_settings.CreateTempFactory(), CONTAINER_SORT_SIZE, _header.Pragmas);

            // initialize transaction monitor as last service
            _monitor = new TransactionMonitor(_header, _locker, _disk, _walIndex);

            // register system collections
            InitializeSystemCollections();

            LOG("initialization completed", "ENGINE");

            return true;
        }
        catch (Exception ex)
        {
            LOG(ex.Message, "ERROR");

            Close(ex);

            throw;
        }
    }

    /// <summary>
    /// Normal close process:
    /// - Stop any new transaction
    /// - Stop operation loops over database (throw in SafePoint)
    /// - Wait for writer queue
    /// - Close disks
    /// - Clean variables
    /// </summary>
    internal List<Exception> Close()
    {
        if (_state == null || _state.Disposed)
        {
            return [];
        }

        _state.Disposed = true;

        var tc = new TryCatch();

        // stop running all transactions
        tc.Catch(() => _monitor?.Dispose());

        if (_header?.Pragmas.Checkpoint > 0)
        {
            // do a soft checkpoint (only if exclusive lock is possible)
            tc.Catch(() => _walIndex?.TryCheckpoint().GetAwaiter().GetResult());
        }

        // close all disk streams (and delete log if empty)
        tc.Catch(() => _disk?.Dispose());

        // delete sort temp file
        tc.Catch(() => _sortDisk?.Dispose());

        // dispose lockers
        tc.Catch(() => _locker?.Dispose());

        return tc.Exceptions;
    }

    /// <summary>
    /// Async close — uses <see cref="WalIndexService.TryCheckpoint"/> and
    /// <see cref="DiskService.DisposeAsync"/> so shutdown I/O does not block a thread.
    /// Phase 3 addition.
    /// </summary>
    internal async ValueTask<List<Exception>> CloseAsync()
    {
        if (_state == null || _state.Disposed)
            return [];

        _state.Disposed = true;

        var exceptions = new System.Collections.Generic.List<Exception>();

        try { _monitor?.Dispose(); }
        catch (Exception ex) { exceptions.Add(ex); }

        if (_header?.Pragmas.Checkpoint > 0 && _walIndex != null)
        {
            try { await _walIndex.TryCheckpoint().ConfigureAwait(false); }
            catch (Exception ex) { exceptions.Add(ex); }
        }

        if (_disk != null)
        {
            try { await _disk.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { exceptions.Add(ex); }
        }

        try { _sortDisk?.Dispose(); }
        catch (Exception ex) { exceptions.Add(ex); }

        try { _locker?.Dispose(); }
        catch (Exception ex) { exceptions.Add(ex); }

        return exceptions;
    }

    /// <summary>
    /// Exception close database:
    /// - Stop diskQueue
    /// - Stop any disk read/write (dispose)
    /// - Dispose sort disk
    /// - Dispose locker
    /// - Checks Exception type for INVALID_DATAFILE_STATE to auto rebuild on open
    /// </summary>
    internal List<Exception> Close(Exception ex)
    {
        if (_state == null || _state.Disposed)
        {
            return new List<Exception>();
        }

        _state.Disposed = true;

        var tc = new TryCatch(ex);

        tc.Catch(() => _monitor?.Dispose());

        // close disks streams
        tc.Catch(() => _disk?.Dispose());

        // close sort disk service
        tc.Catch(() => _sortDisk?.Dispose());

        // close engine lock service
        tc.Catch(() => _locker?.Dispose());

        if (tc.InvalidDatafileState)
        {
            // mark byte = 1 in HeaderPage.P_INVALID_DATAFILE_STATE - will open in auto-rebuild
            // this method will throw no errors
            tc.Catch(() => _disk.MarkAsInvalidState());
        }

        return tc.Exceptions;
    }

    internal async ValueTask<List<Exception>> CloseAsync(Exception ex, CancellationToken cancellationToken = default)
    {
        if (_state == null || _state.Disposed)
        {
            return [];
        }

        _state.Disposed = true;

        var tc = new TryCatch(ex);

        tc.Catch(() => _monitor?.Dispose());

        await tc.CatchAsync(async () =>
        {
            if (_disk != null)
            {
                await _disk.DisposeAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);

        tc.Catch(() => _sortDisk?.Dispose());
        tc.Catch(() => _locker?.Dispose());

        if (tc.InvalidDatafileState)
        {
            await tc.CatchAsync(async () =>
            {
                if (_disk != null)
                {
                    await _disk.MarkAsInvalidStateAsync(cancellationToken).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }

        return tc.Exceptions;
    }

    #endregion

#if DEBUG
    // exposes for unit tests
    internal TransactionMonitor GetMonitor()
    {
        return _monitor;
    }

    internal Action<PageBuffer> SimulateDiskReadFail
    {
        set => _state.SimulateDiskReadFail = value;
    }

    internal Action<PageBuffer> SimulateDiskWriteFail
    {
        set => _state.SimulateDiskWriteFail = value;
    }
#endif
}