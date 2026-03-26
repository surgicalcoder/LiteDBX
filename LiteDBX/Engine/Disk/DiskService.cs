using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

/// <summary>
/// Implement custom fast/in memory mapped disk access.
/// [ThreadSafe]
///
/// Phase 3 changes:
/// <list type="bullet">
///   <item><see cref="WriteLogDisk"/> is now async; <c>lock(stream)</c> replaced with
///     <see cref="SemaphoreSlim"/> so the caller is never blocked on a thread.</item>
///   <item><see cref="WriteDataDisk"/> is now async using <see cref="Stream.WriteAsync"/>
///     and <see cref="StreamExtensions.FlushToDiskAsync"/>.</item>
///   <item><see cref="ReadFullAsync"/> added as the async enumeration path for operational
///     runtime reads (checkpoint, restore-index bootstrap path kept sync via <see cref="ReadFull"/>).</item>
///   <item><see cref="MarkAsInvalidStateAsync"/> replaces the sync version for the error-close path.</item>
///   <item>Implements <see cref="IAsyncDisposable"/> for the engine async-close lifecycle.</item>
/// </list>
/// </summary>
internal class DiskService : IDisposable, IAsyncDisposable
{
    private static readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;

    private readonly IStreamFactory _dataFactory;
    private readonly StreamPool _dataPool;
    private readonly IStreamFactory _logFactory;
    private readonly StreamPool _logPool;
    private readonly EngineState _state;
    private readonly Lazy<Stream> _writer;

    /// <summary>
    /// Serialises all concurrent WAL writes so that page positions are assigned atomically and
    /// written in order.  Replaces the former <c>lock(stream)</c> block.
    /// </summary>
    private readonly SemaphoreSlim _writerGate = new SemaphoreSlim(1, 1);

    private long _dataLength;
    private long _logLength;

    public DiskService(
        EngineSettings settings,
        EngineState state,
        int[] memorySegmentSizes)
    {
        Cache = new MemoryCache(memorySegmentSizes, settings.MemoryCacheConfig);
        _state = state;

        _dataFactory = settings.CreateDataFactory();
        _logFactory = settings.CreateLogFactory();

        _dataPool = new StreamPool(_dataFactory, false);
        _logPool = new StreamPool(_logFactory, true);

        _writer = _logPool.Writer;

        var isNew = _dataFactory.GetLength() == 0L;

        if (isNew)
        {
            LOG($"creating new database: '{Path.GetFileName(_dataFactory.Name)}'", "DISK");
            Initialize(_dataPool.Writer.Value, settings.Collation, settings.InitialSize);
        }

        if (!settings.ReadOnly)
        {
            _ = _dataPool.Writer.Value.CanRead;
        }

        _dataLength = _dataFactory.GetLength() - PAGE_SIZE;

        if (_logFactory.Exists())
        {
            _logLength = _logFactory.GetLength() - PAGE_SIZE;
        }
        else
        {
            _logLength = -PAGE_SIZE;
        }
    }

    /// <summary>Get memory cache instance</summary>
    public MemoryCache Cache { get; }

    /// <summary>
    /// Maximum number of items (documents or IndexNodes) this database can have.
    /// Used to prevent infinite loops in case of pointer problems.
    /// </summary>
    public uint MAX_ITEMS_COUNT => (uint)((_dataLength + _logLength) / PAGE_SIZE + 10) * byte.MaxValue;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Dispose()
    {
        var delete = _logFactory.Exists() && _logPool.Writer.Value.Length == 0;

        _dataPool.Dispose();
        _logPool.Dispose();

        if (delete)
            _logFactory.Delete();

        _writerGate.Dispose();
        Cache.Dispose();
    }

    /// <summary>
    /// Async dispose — preferred on the engine shutdown path.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        var delete = _logFactory.Exists() && _logPool.Writer.Value.Length == 0;

        await _dataPool.DisposeAsync().ConfigureAwait(false);
        await _logPool.DisposeAsync().ConfigureAwait(false);

        if (delete)
            _logFactory.Delete();

        _writerGate.Dispose();
        Cache.Dispose();
    }

    // ── Initialisation (startup-only, sync) ───────────────────────────────────

    /// <summary>
    /// Create a new empty database (synchronous; called only once on first open).
    /// </summary>
    private void Initialize(Stream stream, Collation collation, long initialSize)
    {
        var buffer = new PageBuffer(new byte[PAGE_SIZE], 0, 0);
        var header = new HeaderPage(buffer, 0);

        header.Pragmas.Set(Pragmas.COLLATION, (collation ?? Collation.Default).ToString(), false);
        header.UpdateBuffer();

        stream.Write(buffer.Array, buffer.Offset, PAGE_SIZE);

        if (initialSize > 0)
        {
            if (stream is AesStream)
                throw LiteException.InitialSizeCryptoNotSupported();

            if (initialSize % PAGE_SIZE != 0)
                throw LiteException.InvalidInitialSize();

            stream.SetLength(initialSize);
        }

        stream.FlushToDisk();
    }

    // ── Reader factory ────────────────────────────────────────────────────────

    /// <summary>
    /// Get a new instance for reading data/log pages.
    /// The instance is NOT thread-safe — one per async execution context (transaction).
    /// </summary>
    public DiskReader GetReader()
    {
        return new DiskReader(_state, Cache, _dataPool, _logPool);
    }

    // ── Cache helpers (non-I/O) ───────────────────────────────────────────────

    public void DiscardDirtyPages(IEnumerable<PageBuffer> pages)
    {
        foreach (var page in pages)
            Cache.DiscardPage(page);
    }

    public void DiscardCleanPages(IEnumerable<PageBuffer> pages)
    {
        foreach (var page in pages)
        {
            if (!Cache.TryMoveToReadable(page))
                Cache.DiscardPage(page);
        }
    }

    public PageBuffer NewPage() => Cache.NewPage();

    // ── Async WAL write (Phase 3 primary path) ────────────────────────────────

    /// <summary>
    /// Write all pages to the log file.
    /// Uses a <see cref="SemaphoreSlim"/> rather than <c>lock</c> so the caller never blocks
    /// a thread-pool thread while waiting for a concurrent write to finish.
    /// </summary>
    public async ValueTask<int> WriteLogDisk(IEnumerable<PageBuffer> pages,
        CancellationToken cancellationToken = default)
    {
        var count = 0;
        var stream = _writer.Value;

        await _writerGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var page in pages)
            {
                ENSURE(page.ShareCounter == BUFFER_WRITABLE, "to enqueue page, page must be writable");

                page.Position = Interlocked.Add(ref _logLength, PAGE_SIZE);
                page.Origin = FileOrigin.Log;

                Cache.MoveToReadable(page);

                stream.Position = page.Position;

#if DEBUG
                _state.SimulateDiskWriteFail?.Invoke(page);
#endif

                await stream.WriteAsync(page.Array, page.Offset, PAGE_SIZE, cancellationToken)
                            .ConfigureAwait(false);

                page.Release();
                count++;
            }
        }
        finally
        {
            _writerGate.Release();
        }

        return count;
    }

    // ── Sync WAL write (Phase 4 bridge) ──────────────────────────────────────

    /// <summary>
    /// Phase 4 bridge: synchronous WAL write for code paths not yet converted to async.
    /// All new code should use <see cref="WriteLogDisk(IEnumerable{PageBuffer},CancellationToken)"/>.
    /// </summary>
    internal int WriteLogDiskSync(IEnumerable<PageBuffer> pages)
    {
        var count = 0;
        var stream = _writer.Value;

        _writerGate.Wait();
        try
        {
            foreach (var page in pages)
            {
                ENSURE(page.ShareCounter == BUFFER_WRITABLE, "to enqueue page, page must be writable");

                page.Position = Interlocked.Add(ref _logLength, PAGE_SIZE);
                page.Origin = FileOrigin.Log;

                Cache.MoveToReadable(page);

                stream.Position = page.Position;

#if DEBUG
                _state.SimulateDiskWriteFail?.Invoke(page);
#endif

                stream.Write(page.Array, page.Offset, PAGE_SIZE);

                page.Release();
                count++;
            }
        }
        finally
        {
            _writerGate.Release();
        }

        return count;
    }

    // ── Async data-file write (Phase 3 primary path) ──────────────────────────

    /// <summary>
    /// Write pages DIRECTLY to the data file (checkpoint path). Pages are not cached.
    /// Uses <see cref="Stream.WriteAsync"/> and an async flush to avoid blocking.
    /// </summary>
    public async ValueTask WriteDataDisk(IEnumerable<PageBuffer> pages,
        CancellationToken cancellationToken = default)
    {
        var stream = _dataPool.Writer.Value;

        foreach (var page in pages)
        {
            ENSURE(page.ShareCounter == 0,
                "this page can't be shared to use sync operation - do not use cached pages");

            _dataLength = Math.Max(_dataLength, page.Position);

            stream.Position = page.Position;
            await stream.WriteAsync(page.Array, page.Offset, PAGE_SIZE, cancellationToken)
                        .ConfigureAwait(false);
        }

        await stream.FlushToDiskAsync(cancellationToken).ConfigureAwait(false);
    }

    // ── Sync ReadFull (startup/restore-index bridge) ──────────────────────────

    /// <summary>
    /// Read all database pages from <paramref name="origin"/> sequentially with no cache.
    /// Kept synchronous for the engine startup / RestoreIndex path which runs before the
    /// async lifecycle is active.  Use <see cref="ReadFullAsync"/> in all runtime paths.
    /// </summary>
    public IEnumerable<PageBuffer> ReadFull(FileOrigin origin)
    {
        var buffer = new byte[PAGE_SIZE];
        var pool = origin == FileOrigin.Log ? _logPool : _dataPool;
        var stream = pool.Rent();

        try
        {
            var length = GetFileLength(origin);
            stream.Position = 0;

            while (stream.Position < length)
            {
                var position = stream.Position;
                var bytesRead = stream.Read(buffer, 0, PAGE_SIZE);

                ENSURE(bytesRead == PAGE_SIZE, "ReadFull must read PAGE_SIZE bytes [{0}]", bytesRead);

                yield return new PageBuffer(buffer, 0, 0)
                {
                    Position = position,
                    Origin = origin,
                    ShareCounter = 0
                };
            }
        }
        finally
        {
            pool.Return(stream);
        }
    }

    // ── Async ReadFull (Phase 3 primary runtime path) ─────────────────────────

    /// <summary>
    /// Asynchronously enumerate all pages in the given file origin.
    /// Pages are streamed with no cache involvement; <c>PageBuffer</c> instances must NOT be
    /// <c>Release()</c>'d by the caller because they are not cache-tracked.
    /// Used by the checkpoint and WAL-index restore paths.
    /// </summary>
    public async IAsyncEnumerable<PageBuffer> ReadFullAsync(FileOrigin origin,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = new byte[PAGE_SIZE];
        var pool = origin == FileOrigin.Log ? _logPool : _dataPool;
        var stream = pool.Rent();

        try
        {
            var length = GetFileLength(origin);
            stream.Position = 0;

            while (stream.Position < length)
            {
                var position = stream.Position;
                var bytesRead = await stream.ReadAsync(buffer, 0, PAGE_SIZE, cancellationToken)
                                            .ConfigureAwait(false);

                ENSURE(bytesRead == PAGE_SIZE, "ReadFullAsync must read PAGE_SIZE bytes [{0}]", bytesRead);

                yield return new PageBuffer(buffer, 0, 0)
                {
                    Position = position,
                    Origin = origin,
                    ShareCounter = 0
                };
            }
        }
        finally
        {
            pool.Return(stream);
        }
    }

    // ── Metadata helpers (non-I/O) ────────────────────────────────────────────

    public long GetFileLength(FileOrigin origin)
    {
        if (origin == FileOrigin.Log)
            return _logLength + PAGE_SIZE;
        return _dataLength + PAGE_SIZE;
    }

    public void SetLength(long length, FileOrigin origin)
    {
        var stream = origin == FileOrigin.Log ? _logPool.Writer : _dataPool.Writer;

        if (origin == FileOrigin.Log)
            Interlocked.Exchange(ref _logLength, length - PAGE_SIZE);
        else
            Interlocked.Exchange(ref _dataLength, length - PAGE_SIZE);

        stream.Value.SetLength(length);
    }

    public string GetName(FileOrigin origin)
    {
        return origin == FileOrigin.Data ? _dataFactory.Name : _logFactory.Name;
    }

    // ── Invalid-state marking ─────────────────────────────────────────────────

    /// <summary>
    /// Mark a file with a single signal so the next open triggers auto-rebuild.
    /// Used on abnormal close; sync version kept for the sync Close path.
    /// </summary>
    internal void MarkAsInvalidState()
    {
        FileHelper.TryExec(60, () =>
        {
            using (var stream = _dataFactory.GetStream(true, true))
            {
                var buf = _bufferPool.Rent(PAGE_SIZE);
                stream.Read(buf, 0, PAGE_SIZE);
                buf[HeaderPage.P_INVALID_DATAFILE_STATE] = 1;
                stream.Position = 0;
                stream.Write(buf, 0, PAGE_SIZE);
                _bufferPool.Return(buf, true);
            }
        });
    }

    /// <summary>
    /// Async version of <see cref="MarkAsInvalidState"/> for the async-close path.
    /// </summary>
    internal async ValueTask MarkAsInvalidStateAsync(CancellationToken cancellationToken = default)
    {
        // FileHelper.TryExec retries for up to 60 s; for the async path we perform one attempt
        // and document that retry logic is a Phase 8 (lifecycle) concern.
        try
        {
            // Regular using: stream disposal is synchronous; only the read/write calls are async.
            using var stream = _dataFactory.GetStream(true, true);

            var buf = _bufferPool.Rent(PAGE_SIZE);
            try
            {
                await stream.ReadAsync(buf, 0, PAGE_SIZE, cancellationToken).ConfigureAwait(false);
                buf[HeaderPage.P_INVALID_DATAFILE_STATE] = 1;
                stream.Position = 0;
                await stream.WriteAsync(buf, 0, PAGE_SIZE, cancellationToken).ConfigureAwait(false);
                await stream.FlushToDiskAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _bufferPool.Return(buf, true);
            }
        }
        catch
        {
            // Best-effort; errors are swallowed the same way the sync version swallows them.
        }
    }
}