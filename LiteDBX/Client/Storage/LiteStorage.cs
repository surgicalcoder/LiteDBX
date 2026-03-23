using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using static LiteDbX.Constants;

namespace LiteDbX;

/// <summary>
/// Async-only implementation of <see cref="ILiteStorage{TFileId}"/>.
///
/// Phase 5 redesign notes:
/// ─────────────────────────────────────────────────────────────────────────────
/// • All operations are genuinely async: chunk lookups, inserts, deletes, and
///   metadata persistence use the async <see cref="ILiteCollection{T}"/> methods
///   that were established in Phases 1–4.
/// • The sync <c>LiteFileStream&lt;TFileId&gt; : Stream</c> is no longer used.
///   Read/write access is provided by <see cref="LiteFileHandle{TFileId}"/> via
///   <see cref="ILiteFileHandle{TFileId}"/>.
/// • Upload helpers (<see cref="Upload(TFileId,string,Stream,BsonDocument,CancellationToken)"/>
///   and the filesystem overload) copy data through an async pipeline:
///   a write handle is opened, bytes are read from the source stream with
///   <c>ReadAsync</c>, and written to the handle with its async <c>Write</c>.
/// • Download helpers drain a read handle into the destination stream via
///   async chunk reads.
/// • File metadata is persisted (upserted) by <see cref="LiteFileHandle{TFileId}"/>
///   during its <c>Flush</c> / <c>DisposeAsync</c>.  Callers do not need to
///   perform a separate metadata write after upload.
/// • No sync public methods remain.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class LiteStorage<TFileId> : ILiteStorage<TFileId>
{
    private readonly ILiteCollection<BsonDocument> _chunks;
    private readonly ILiteDatabase _db;
    private readonly ILiteCollection<LiteFileInfo<TFileId>> _files;

    public LiteStorage(ILiteDatabase db, string filesCollection, string chunksCollection)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _files = db.GetCollection<LiteFileInfo<TFileId>>(filesCollection);
        _chunks = db.GetCollection(chunksCollection);
    }

    // ── Lookup ────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async ValueTask<LiteFileInfo<TFileId>> FindById(TFileId id, CancellationToken cancellationToken = default)
    {
        if (id is null) throw new ArgumentNullException(nameof(id));

        var fileId = _db.Mapper.Serialize(typeof(TFileId), id);
        return await _files.FindById(fileId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<LiteFileInfo<TFileId>> Find(
        BsonExpression predicate,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var query = _files.Query();
        if (predicate != null)
            query = query.Where(predicate);

        await foreach (var file in query.ToEnumerable(cancellationToken).ConfigureAwait(false))
        {
            yield return file;
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<LiteFileInfo<TFileId>> Find(
        string predicate,
        BsonDocument parameters,
        CancellationToken cancellationToken = default)
        => Find(BsonExpression.Create(predicate, parameters), cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<LiteFileInfo<TFileId>> Find(string predicate, params BsonValue[] args)
        => Find(BsonExpression.Create(predicate, args));

    /// <inheritdoc/>
    public IAsyncEnumerable<LiteFileInfo<TFileId>> Find(
        Expression<Func<LiteFileInfo<TFileId>, bool>> predicate,
        CancellationToken cancellationToken = default)
        => Find(_db.Mapper.GetExpression(predicate), cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<LiteFileInfo<TFileId>> FindAll(CancellationToken cancellationToken = default)
        => Find((BsonExpression)null, cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<bool> Exists(TFileId id, CancellationToken cancellationToken = default)
    {
        if (id is null) throw new ArgumentNullException(nameof(id));

        var fileId = _db.Mapper.Serialize(typeof(TFileId), id);
        return await _files.Exists("_id = @0", fileId).ConfigureAwait(false);
    }

    // ── Open handles ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async ValueTask<ILiteFileHandle<TFileId>> OpenWrite(
        TFileId id,
        string filename,
        BsonDocument metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (id is null) throw new ArgumentNullException(nameof(id));
        if (filename.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(filename));

        var fileId = _db.Mapper.Serialize(typeof(TFileId), id);
        var existing = await _files.FindById(fileId, cancellationToken).ConfigureAwait(false);

        LiteFileInfo<TFileId> fileInfo;

        if (existing is null)
        {
            fileInfo = new LiteFileInfo<TFileId>
            {
                Id = id,
                Filename = Path.GetFileName(filename),
                MimeType = MimeTypeConverter.GetMimeType(filename),
                Metadata = metadata ?? new BsonDocument()
            };
        }
        else
        {
            fileInfo = existing;
            fileInfo.Filename = Path.GetFileName(filename);
            fileInfo.MimeType = MimeTypeConverter.GetMimeType(filename);
            fileInfo.Metadata = metadata ?? existing.Metadata;

            if (fileInfo.Length > 0)
            {
                // Delete all existing chunks before overwriting.
                var deleted = await _chunks
                    .DeleteMany("_id BETWEEN { f: @0, n: 0 } AND { f: @0, n: 99999999 }", fileId)
                    .ConfigureAwait(false);

                ENSURE(deleted == fileInfo.Chunks);

                fileInfo.Length = 0;
                fileInfo.Chunks = 0;
            }
        }

        return LiteFileHandle<TFileId>.CreateWriter(_files, _chunks, fileInfo, fileId);
    }

    /// <inheritdoc/>
    public async ValueTask<ILiteFileHandle<TFileId>> OpenRead(
        TFileId id,
        CancellationToken cancellationToken = default)
    {
        if (id is null) throw new ArgumentNullException(nameof(id));

        var fileId = _db.Mapper.Serialize(typeof(TFileId), id);
        var fileInfo = await _files.FindById(fileId, cancellationToken).ConfigureAwait(false);

        if (fileInfo is null)
            throw LiteException.FileNotFound(id.ToString());

        return LiteFileHandle<TFileId>.CreateReader(_files, _chunks, fileInfo, fileId);
    }

    // ── Upload ────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async ValueTask<LiteFileInfo<TFileId>> Upload(
        TFileId id,
        string filename,
        Stream stream,
        BsonDocument metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));

        await using var writer = await OpenWrite(id, filename, metadata, cancellationToken).ConfigureAwait(false);

        var buffer = new byte[LiteFileHandle<TFileId>.MaxChunkSize];
        int read;

        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await writer.Write(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        await writer.Flush(cancellationToken).ConfigureAwait(false);

        return writer.FileInfo;
    }

    /// <inheritdoc/>
    public async ValueTask<LiteFileInfo<TFileId>> Upload(
        TFileId id,
        string filename,
        CancellationToken cancellationToken = default)
    {
        if (filename.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(filename));

        // File.OpenRead uses a FileStream; wrap the open in a using so the OS handle is released.
        // Note: FileStream does not implement IAsyncDisposable on netstandard2.0 — use sync using.
        using var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: LiteFileHandle<TFileId>.MaxChunkSize, useAsync: true);

        return await Upload(id, Path.GetFileName(filename), fs, null, cancellationToken).ConfigureAwait(false);
    }

    // ── Metadata ──────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async ValueTask<bool> SetMetadata(
        TFileId id,
        BsonDocument metadata,
        CancellationToken cancellationToken = default)
    {
        var fileInfo = await FindById(id, cancellationToken).ConfigureAwait(false);

        if (fileInfo is null)
            return false;

        fileInfo.Metadata = metadata ?? new BsonDocument();
        await _files.Update(fileInfo, cancellationToken).ConfigureAwait(false);
        return true;
    }

    // ── Download ──────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async ValueTask<LiteFileInfo<TFileId>> Download(
        TFileId id,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));

        await using var reader = await OpenRead(id, cancellationToken).ConfigureAwait(false);

        var buffer = new byte[LiteFileHandle<TFileId>.MaxChunkSize];
        int read;

        while ((read = await reader.Read(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            await stream.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
        }

        return reader.FileInfo;
    }

    /// <inheritdoc/>
    public async ValueTask<LiteFileInfo<TFileId>> Download(
        TFileId id,
        string filename,
        bool overwritten,
        CancellationToken cancellationToken = default)
    {
        if (filename.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(filename));

        var fileMode = overwritten ? FileMode.Create : FileMode.CreateNew;

        // Note: FileStream does not implement IAsyncDisposable on netstandard2.0 — use sync using.
        using var fs = new FileStream(filename, fileMode, FileAccess.Write, FileShare.None,
            bufferSize: LiteFileHandle<TFileId>.MaxChunkSize, useAsync: true);

        return await Download(id, fs, cancellationToken).ConfigureAwait(false);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async ValueTask<bool> Delete(TFileId id, CancellationToken cancellationToken = default)
    {
        if (id is null) throw new ArgumentNullException(nameof(id));

        var fileId = _db.Mapper.Serialize(typeof(TFileId), id);
        var deleted = await _files.Delete(fileId, cancellationToken).ConfigureAwait(false);

        if (deleted)
        {
            await _chunks
                .DeleteMany("_id BETWEEN { f: @0, n: 0 } AND { f: @0, n: @1 }", fileId, new BsonValue(int.MaxValue))
                .ConfigureAwait(false);
        }

        return deleted;
    }
}

