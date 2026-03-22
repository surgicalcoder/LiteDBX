using System;

namespace LiteDbX;

/// <summary>
/// Metadata record for a file entry stored in LiteDB file storage.
///
/// Phase 5 redesign notes:
/// ─────────────────────────────────────────────────────────────────────────────
/// This class is now a plain data transfer object (POCO/DTO).  The former
/// synchronous helper methods (<c>OpenRead</c>, <c>OpenWrite</c>, <c>CopyTo</c>,
/// <c>SaveAs</c>) and the injected collection references (<c>_files</c>,
/// <c>_chunks</c>, <c>_fileId</c>) have been removed because:
///
///   1. They required injecting <see cref="ILiteCollection{T}"/> references into a
///      data object, creating a hidden dependency that is incompatible with the
///      async-only contract.
///   2. The operations those methods performed (open handles, copy data to a
///      stream, save to disk) are now exposed as async methods on
///      <see cref="ILiteStorage{TFileId}"/> and <see cref="ILiteFileHandle{TFileId}"/>,
///      which is the correct home for them in the redesigned API.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class LiteFileInfo<TFileId>
{
    /// <summary>File identifier (typically a <see cref="string"/> or <see cref="ObjectId"/>).</summary>
    public TFileId Id { get; internal set; }

    /// <summary>Original filename including extension.</summary>
    [BsonField("filename")]
    public string Filename { get; internal set; }

    /// <summary>MIME content type derived from the filename extension at upload time.</summary>
    [BsonField("mimeType")]
    public string MimeType { get; internal set; }

    /// <summary>Total byte length of the file content.</summary>
    [BsonField("length")]
    public long Length { get; internal set; } = 0;

    /// <summary>Number of chunk documents that store the file content.</summary>
    [BsonField("chunks")]
    public int Chunks { get; internal set; } = 0;

    /// <summary>Date and time (UTC) when the file was last written.</summary>
    [BsonField("uploadDate")]
    public DateTime UploadDate { get; internal set; } = DateTime.UtcNow;

    /// <summary>User-supplied metadata document.  Defaults to an empty document.</summary>
    [BsonField("metadata")]
    public BsonDocument Metadata { get; set; } = new BsonDocument();
}