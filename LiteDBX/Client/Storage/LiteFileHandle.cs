using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

/// <summary>
/// Async-only implementation of <see cref="ILiteFileHandle{TFileId}"/>.
///
/// Replaces the former <c>LiteFileStream&lt;TFileId&gt; : Stream</c>.
/// <see cref="System.IO.Stream"/> is not part of the public surface because its abstract contract
/// mandates synchronous <c>Read</c>, <c>Write</c>, and <c>Flush</c> overloads which violate the
/// async-only design rule.
///
/// Design notes — Phase 5:
/// ─────────────────────────────────────────────────────────────────────────────
/// • Chunk size: 255 KB (same as the GridFS-derived constant used previously).
/// • Read mode: chunks are loaded on demand asynchronously when <see cref="Read"/> is called.
///   No chunk data is prefetched at construction time; the first Read() triggers the first fetch.
/// • Write mode: incoming bytes are buffered in a <see cref="MemoryStream"/> (CPU-only, no I/O).
///   Once the buffer reaches <see cref="MaxChunkSize"/> a chunk is persisted to the collection.
///   Remaining buffered bytes are flushed and file metadata is persisted when
///   <see cref="Flush"/> is called or when the handle is disposed via <see cref="DisposeAsync"/>.
/// • Seek: supported for read handles only.  It is a pure position-state change (O(1)).
///   The chunk that contains the new position is loaded lazily on the next <see cref="Read"/>.
///   Seek is not supported on write handles (throws <see cref="NotSupportedException"/>).
/// • Finalization safety: <see cref="DisposeAsync"/> calls <see cref="Flush"/> exactly once,
///   making it safe to dispose without an explicit flush.  Double-dispose is a no-op.
/// • Thread safety: this class is NOT thread-safe.  Callers must not share a handle across
///   concurrent async operations.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
internal sealed class LiteFileHandle<TFileId> : ILiteFileHandle<TFileId>
{
    /// <summary>Maximum number of bytes stored in a single chunk document (255 KB, same as GridFS default).</summary>
    public const int MaxChunkSize = 255 * 1024;

    private readonly ILiteCollection<BsonDocument> _chunks;
    private readonly ILiteCollection<LiteFileInfo<TFileId>> _files;
    private readonly BsonValue _fileId;

    // ── Shared state ──────────────────────────────────────────────────────────

    private long _position;
    private bool _disposed;

    // ── Read state ────────────────────────────────────────────────────────────

    /// <summary>Binary data of the currently loaded chunk, or null when no chunk is loaded.</summary>
    private byte[] _currentChunkData;

    /// <summary>Zero-based index of the currently loaded chunk, or -1 when no chunk is loaded.</summary>
    private int _currentChunkIndex = -1;

    /// <summary>Byte offset within <see cref="_currentChunkData"/> for the next read.</summary>
    private int _positionInChunk;

    /// <summary>Lengths of already-loaded chunks, keyed by chunk index.  Used for accurate seek calculation.</summary>
    private readonly Dictionary<int, int> _chunkLengths = new();

    // ── Write state ───────────────────────────────────────────────────────────

    private MemoryStream _writeBuffer;
    private bool _finalized;

    // ── Construction ─────────────────────────────────────────────────────────

    private LiteFileHandle(
        ILiteCollection<LiteFileInfo<TFileId>> files,
        ILiteCollection<BsonDocument> chunks,
        LiteFileInfo<TFileId> fileInfo,
        BsonValue fileId,
        bool canWrite)
    {
        _files = files;
        _chunks = chunks;
        FileInfo = fileInfo;
        _fileId = fileId;
        CanWrite = canWrite;
        CanRead = !canWrite;

        if (canWrite)
        {
            _writeBuffer = new MemoryStream(MaxChunkSize);
        }
    }

    /// <summary>Create a read-only handle for the given file.</summary>
    internal static LiteFileHandle<TFileId> CreateReader(
        ILiteCollection<LiteFileInfo<TFileId>> files,
        ILiteCollection<BsonDocument> chunks,
        LiteFileInfo<TFileId> fileInfo,
        BsonValue fileId)
        => new LiteFileHandle<TFileId>(files, chunks, fileInfo, fileId, canWrite: false);

    /// <summary>
    /// Create a write handle for the given file.
    /// Old chunk deletion (if overwriting) must be performed by the caller <em>before</em> creating the handle.
    /// </summary>
    internal static LiteFileHandle<TFileId> CreateWriter(
        ILiteCollection<LiteFileInfo<TFileId>> files,
        ILiteCollection<BsonDocument> chunks,
        LiteFileInfo<TFileId> fileInfo,
        BsonValue fileId)
        => new LiteFileHandle<TFileId>(files, chunks, fileInfo, fileId, canWrite: true);

    // ── ILiteFileHandle<TFileId> — properties ─────────────────────────────────

    /// <inheritdoc/>
    public LiteFileInfo<TFileId> FileInfo { get; }

    /// <inheritdoc/>
    public bool CanRead { get; }

    /// <inheritdoc/>
    public bool CanWrite { get; }

    /// <inheritdoc/>
    public long Length => FileInfo.Length;

    /// <inheritdoc/>
    public long Position => _position;

    // ── ILiteFileHandle<TFileId> — Read ───────────────────────────────────────

    /// <inheritdoc/>
    public async ValueTask<int> Read(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ThrowIfNotReadMode();

        if (buffer.IsEmpty || _position >= FileInfo.Length)
            return 0;

        var targetChunkIndex = EstimateChunkIndex(_position);

        if (_currentChunkIndex != targetChunkIndex || _currentChunkData == null)
        {
            await LoadChunk(targetChunkIndex, cancellationToken).ConfigureAwait(false);
        }

        if (_currentChunkData == null)
            return 0;

        var totalRead = 0;
        var destination = buffer;

        while (!destination.IsEmpty && _currentChunkData != null && _position < FileInfo.Length)
        {
            var available = _currentChunkData.Length - _positionInChunk;
            var toCopy = Math.Min(available, destination.Length);

            _currentChunkData.AsMemory(_positionInChunk, toCopy).CopyTo(destination);
            destination = destination.Slice(toCopy);
            _positionInChunk += toCopy;
            _position += toCopy;
            totalRead += toCopy;

            if (_positionInChunk >= _currentChunkData.Length)
            {
                _positionInChunk = 0;
                var nextIndex = _currentChunkIndex + 1;

                if (_position < FileInfo.Length)
                {
                    await LoadChunk(nextIndex, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    _currentChunkData = null;
                    _currentChunkIndex = -1;
                }
            }
        }

        return totalRead;
    }

    // ── ILiteFileHandle<TFileId> — Write ──────────────────────────────────────

    /// <inheritdoc/>
    public async ValueTask Write(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ThrowIfNotWriteMode();

        if (buffer.IsEmpty)
            return;

        _position += buffer.Length;

        // MemoryStream.WriteAsync accepts byte[] rather than ReadOnlyMemory<byte> on .NET Standard 2.x,
        // so we use a temporary array.  On .NET 5+ this could be optimised with GetSpan / Advance.
        var bytes = buffer.ToArray();
        await _writeBuffer.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);

        if (_writeBuffer.Length >= MaxChunkSize)
        {
            await PersistBufferedChunks(finalize: false, cancellationToken).ConfigureAwait(false);
        }
    }

    // ── ILiteFileHandle<TFileId> — Flush ──────────────────────────────────────

    /// <inheritdoc/>
    public ValueTask Flush(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!CanWrite)
            return default; // no-op for read handles — returns a completed ValueTask on all TFMs

        return PersistBufferedChunks(finalize: true, cancellationToken);
    }

    // ── ILiteFileHandle<TFileId> — Seek ───────────────────────────────────────

    /// <inheritdoc/>
    public ValueTask Seek(long position, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!CanRead)
            throw new NotSupportedException("Seeking is not supported on write handles.");

        if (position < 0)
            throw new ArgumentOutOfRangeException(nameof(position));

        // Clamp to [0, Length].
        _position = Math.Min(position, FileInfo.Length);

        // Invalidate the current chunk so the next Read() triggers a fresh load.
        _currentChunkData = null;
        _currentChunkIndex = -1;
        _positionInChunk = 0;

        return default; // pure state change — returns a completed ValueTask on all TFMs
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    /// <summary>
    /// Flush any pending write data and finalize file metadata, then release all resources.
    /// Safe to call multiple times (second call is a no-op).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (CanWrite && !_finalized)
        {
            // Best-effort flush; if the write session was abandoned without an explicit Flush()
            // we still attempt to commit whatever data was buffered.
            await PersistBufferedChunks(finalize: true, CancellationToken.None).ConfigureAwait(false);
        }

        _writeBuffer?.Dispose();
        _writeBuffer = null;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Load chunk at <paramref name="chunkIndex"/> from the chunks collection.
    /// Updates <see cref="_currentChunkData"/>, <see cref="_currentChunkIndex"/>,
    /// <see cref="_positionInChunk"/>, and <see cref="_chunkLengths"/>.
    /// </summary>
    private async ValueTask LoadChunk(int chunkIndex, CancellationToken cancellationToken)
    {
        var chunk = await _chunks
            .FindOne(
                BsonExpression.Create("_id = { f: @0, n: @1 }", _fileId, new BsonValue(chunkIndex)),
                cancellationToken)
            .ConfigureAwait(false);

        if (chunk is null)
        {
            _currentChunkData = null;
            _currentChunkIndex = -1;
            return;
        }

        _currentChunkData = chunk["data"].AsBinary;
        _currentChunkIndex = chunkIndex;
        _positionInChunk = 0;

        if (_currentChunkData != null)
        {
            _chunkLengths[chunkIndex] = _currentChunkData.Length;
        }
    }

    /// <summary>
    /// Estimate the chunk index that contains <paramref name="position"/> within the file.
    /// Uses cached chunk lengths for accuracy; falls back to uniform-chunk-size division for
    /// chunks not yet loaded.
    /// </summary>
    private int EstimateChunkIndex(long position)
    {
        if (position == 0)
            return 0;

        long cumulative = 0;

        for (int i = 0; ; i++)
        {
            if (_chunkLengths.TryGetValue(i, out var chunkLen))
            {
                if (cumulative + chunkLen > position)
                    return i;

                cumulative += chunkLen;
            }
            else
            {
                // Remaining lengths are unknown; use MaxChunkSize as the estimate.
                return i + (int)((position - cumulative) / MaxChunkSize);
            }
        }
    }

    /// <summary>
    /// Drain <see cref="_writeBuffer"/> into the chunks collection.
    /// When <paramref name="finalize"/> is <c>true</c>, updates file metadata in <c>_files</c>
    /// and sets <see cref="_finalized"/> so subsequent calls are no-ops.
    /// </summary>
    private async ValueTask PersistBufferedChunks(bool finalize, CancellationToken cancellationToken)
    {
        var readBuf = new byte[MaxChunkSize];
        _writeBuffer.Seek(0, SeekOrigin.Begin);

        int read;
        while ((read = await _writeBuffer.ReadAsync(readBuf, 0, MaxChunkSize, cancellationToken).ConfigureAwait(false)) > 0)
        {
            var chunkId = new BsonDocument
            {
                ["f"] = _fileId,
                ["n"] = new BsonValue(FileInfo.Chunks++) // zero-based, incremented per chunk stored
            };

            byte[] chunkData;
            if (read == MaxChunkSize)
            {
                chunkData = readBuf;
                readBuf = new byte[MaxChunkSize]; // allocate fresh buffer to avoid aliasing
            }
            else
            {
                chunkData = new byte[read];
                Buffer.BlockCopy(readBuf, 0, chunkData, 0, read);
            }

            var chunkDoc = new BsonDocument
            {
                ["_id"] = chunkId,
                ["data"] = new BsonValue(chunkData)
            };

            await _chunks.Insert(chunkDoc, cancellationToken).ConfigureAwait(false);
        }

        if (finalize && !_finalized)
        {
            _finalized = true;
            FileInfo.UploadDate = DateTime.UtcNow;
            FileInfo.Length = _position;
            await _files.Upsert(FileInfo, cancellationToken).ConfigureAwait(false);
        }

        // Reset the write buffer for subsequent Write() calls before a final Flush().
        _writeBuffer?.Dispose();
        _writeBuffer = new MemoryStream();
    }

    // ── Guard helpers ─────────────────────────────────────────────────────────

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LiteFileHandle<TFileId>));
    }

    private void ThrowIfNotReadMode()
    {
        if (!CanRead)
            throw new InvalidOperationException("This handle is not open for reading.");
    }

    private void ThrowIfNotWriteMode()
    {
        if (!CanWrite)
            throw new InvalidOperationException("This handle is not open for writing.");
    }
}

