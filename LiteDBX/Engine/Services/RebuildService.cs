using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

/// <summary>
/// Rebuilds a LiteDB database by reading from the existing file and writing a fresh copy.
///
/// Phase 6:
///   <see cref="RebuildAsync"/> is the primary path — it awaits all async engine operations
///   (Pragma, Insert, Checkpoint) without any <c>GetAwaiter().GetResult()</c> calls.
///
///   <see cref="Rebuild"/> (sync) is retained exclusively for the <c>Recovery()</c> call
///   inside <see cref="LiteEngine.Open()"/>, which runs synchronously from the constructor.
///   Full async engine initialisation (static factory method) is deferred to Phase 7.
///   Do not call <see cref="Rebuild"/> from any other site.
/// [ThreadSafe]
/// </summary>
internal class RebuildService
{
    private readonly int _fileVersion;
    private readonly EngineSettings _settings;

    public RebuildService(EngineSettings settings)
    {
        _settings = settings;

        var bufferV7 = ReadFirstBytes(false);

        if (FileReaderV7.IsVersion(bufferV7))
        {
            _fileVersion = 7;
            return;
        }

        var buffer = ReadFirstBytes();
        _fileVersion = FileReaderV8.IsVersion(buffer) ? 8 : throw LiteException.InvalidDatabase();
    }

    // ── Async path (Phase 6 primary) ──────────────────────────────────────────

    /// <summary>
    /// Rebuild the database fully asynchronously. All engine calls are awaited.
    /// File-system operations (File.Move, File.Exists) remain synchronous because
    /// <see cref="File.Move"/> has no built-in async equivalent and runs quickly
    /// relative to the overall rebuild duration.
    /// </summary>
    public async Task<long> RebuildAsync(
        RebuildOptions options,
        CancellationToken cancellationToken = default)
    {
        var backupFilename    = FileHelper.GetSuffixFile(_settings.Filename, "-backup");
        var backupLogFilename = FileHelper.GetSuffixFile(FileHelper.GetLogFile(_settings.Filename), "-backup");
        var tempFilename      = FileHelper.GetSuffixFile(_settings.Filename);

        await using var reader = _fileVersion == 7
            ? (IFileReader)new FileReaderV7(_settings)
            : new FileReaderV8(_settings, options.Errors);

        reader.Open();

        await using var engine = new LiteEngine(new EngineSettings
        {
            Filename   = tempFilename,
            Collation  = options.Collation,
            Password   = options.Password
        });

        // Disable checkpoint during rebuild so log pages accumulate.
        await engine.Pragma(Pragmas.CHECKPOINT, 0, cancellationToken).ConfigureAwait(false);

        // Copy all content from the reader into the new engine (truly async).
        await engine.RebuildContentAsync(reader, cancellationToken).ConfigureAwait(false);

        // Insert error report if requested.
        if (options.IncludeErrorReport && options.Errors.Count > 0)
        {
            var report = options.GetErrorReport();
            await engine.Insert("_rebuild_errors", report, BsonAutoId.Int32, cancellationToken)
                        .ConfigureAwait(false);
        }

        // Restore pragmas from the source file.
        var pragmas = reader.GetPragmas();
        await engine.Pragma(Pragmas.CHECKPOINT,  pragmas[Pragmas.CHECKPOINT],  cancellationToken).ConfigureAwait(false);
        await engine.Pragma(Pragmas.TIMEOUT,     pragmas[Pragmas.TIMEOUT],     cancellationToken).ConfigureAwait(false);
        await engine.Pragma(Pragmas.LIMIT_SIZE,  pragmas[Pragmas.LIMIT_SIZE],  cancellationToken).ConfigureAwait(false);
        await engine.Pragma(Pragmas.UTC_DATE,    pragmas[Pragmas.UTC_DATE],    cancellationToken).ConfigureAwait(false);
        await engine.Pragma(Pragmas.USER_VERSION,pragmas[Pragmas.USER_VERSION],cancellationToken).ConfigureAwait(false);

        // Flush log into the data file.
        await engine.Checkpoint(cancellationToken).ConfigureAwait(false);

        // engine and reader are disposed here (await using)

        return SwapFiles(_settings.Filename, tempFilename, backupFilename, backupLogFilename);
    }

    // ── Sync path (constructor/Recovery only — Phase 6 deferred) ─────────────

    /// <summary>
    /// Synchronous rebuild used exclusively by <see cref="LiteEngine.Recovery()"/>,
    /// which is called from the synchronous <c>Open()</c> / constructor path.
    ///
    /// Phase 6 deferred: this path calls <c>.GetAwaiter().GetResult()</c> on async
    /// engine methods because the constructor cannot yet be made async. The deferred
    /// item is creating a static <c>LiteEngine.OpenAsync()</c> factory in Phase 7,
    /// which will allow the Recovery path to be converted to <see cref="RebuildAsync"/>.
    ///
    /// Do not call this method except from <c>Recovery()</c>.
    /// </summary>
    internal long Rebuild(RebuildOptions options)
    {
        var backupFilename    = FileHelper.GetSuffixFile(_settings.Filename, "-backup");
        var backupLogFilename = FileHelper.GetSuffixFile(FileHelper.GetLogFile(_settings.Filename), "-backup");
        var tempFilename      = FileHelper.GetSuffixFile(_settings.Filename);

        using var reader = _fileVersion == 7
            ? (IFileReader)new FileReaderV7(_settings)
            : new FileReaderV8(_settings, options.Errors);

        reader.Open();

        using var engine = new LiteEngine(new EngineSettings
        {
            Filename   = tempFilename,
            Collation  = options.Collation,
            Password   = options.Password
        });

        // Phase 6 deferred: sync-over-async on the constructor path only.
        engine.Pragma(Pragmas.CHECKPOINT, 0).GetAwaiter().GetResult();

        engine.RebuildContent(reader);

        if (options.IncludeErrorReport && options.Errors.Count > 0)
        {
            var report = options.GetErrorReport();
            engine.Insert("_rebuild_errors", report, BsonAutoId.Int32).GetAwaiter().GetResult();
        }

        var pragmas = reader.GetPragmas();
        engine.Pragma(Pragmas.CHECKPOINT,  pragmas[Pragmas.CHECKPOINT]).GetAwaiter().GetResult();
        engine.Pragma(Pragmas.TIMEOUT,     pragmas[Pragmas.TIMEOUT]).GetAwaiter().GetResult();
        engine.Pragma(Pragmas.LIMIT_SIZE,  pragmas[Pragmas.LIMIT_SIZE]).GetAwaiter().GetResult();
        engine.Pragma(Pragmas.UTC_DATE,    pragmas[Pragmas.UTC_DATE]).GetAwaiter().GetResult();
        engine.Pragma(Pragmas.USER_VERSION,pragmas[Pragmas.USER_VERSION]).GetAwaiter().GetResult();

        engine.Checkpoint().GetAwaiter().GetResult();

        return SwapFiles(_settings.Filename, tempFilename, backupFilename, backupLogFilename);
    }

    // ── Shared helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Renames files to swap the rebuilt temp file into the primary position.
    /// Returns the size difference (positive = space reclaimed).
    /// </summary>
    private static long SwapFiles(
        string original,
        string temp,
        string backup,
        string backupLog)
    {
        var logFile = FileHelper.GetLogFile(original);

        if (File.Exists(logFile))
        {
            File.Move(logFile, backupLog);
        }

        FileHelper.Exec(5, () => File.Move(original, backup));
        File.Move(temp, original);

        return new FileInfo(backup).Length - new FileInfo(original).Length;
    }

    /// <summary>Read the first 16 KB (2 pages) of the data file for version detection.</summary>
    private byte[] ReadFirstBytes(bool useAesStream = true)
    {
        var buffer  = new byte[PAGE_SIZE * 2];
        var factory = _settings.CreateDataFactory(useAesStream);

        using var stream = factory.GetStream(false, true);
        stream.Position = 0;
        stream.Read(buffer, 0, buffer.Length);

        return buffer;
    }
}

