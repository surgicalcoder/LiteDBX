using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Engine;

public partial class LiteEngine
{
    private static readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;

    /// <summary>
    /// Detect and upgrade a v7 datafile to the current format before the disk service opens.
    /// Used by the explicit <c>LiteEngine.Open(...)</c> lifecycle.
    /// </summary>
    private async ValueTask TryUpgrade(CancellationToken cancellationToken)
    {
        var filename = _settings.Filename;

        if (!File.Exists(filename))
        {
            return;
        }

        const int bufferSize = 1024;
        var buffer = _bufferPool.Rent(bufferSize);

        try
        {
            using (var stream = new FileStream(
                       _settings.Filename,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       bufferSize,
                       FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                stream.Position = 0;

                var bytesRead = 0;
                while (bytesRead < bufferSize)
                {
                    var read = await stream.ReadAsync(buffer, bytesRead, bufferSize - bytesRead, cancellationToken)
                        .ConfigureAwait(false);

                    if (read == 0)
                    {
                        break;
                    }

                    bytesRead += read;
                }

                if (!FileReaderV7.IsVersion(buffer))
                {
                    return;
                }
            }

            await Recovery(_settings.Collation, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _bufferPool.Return(buffer, true);
        }
    }

    /// <summary>
    /// Detect and upgrade a v7 datafile to the current format before the disk service opens.
    ///
    /// This sync overload is retained only for the legacy constructor-based <c>Open()</c>
    /// startup path. The explicit <c>LiteEngine.Open(...)</c> lifecycle uses the async
    /// overload above.
    /// </summary>
    private void TryUpgrade()
    {
        var filename = _settings.Filename;

        // if file not exists, just exit
        if (!File.Exists(filename))
        {
            return;
        }

        const int bufferSize = 1024;
        var buffer = _bufferPool.Rent(bufferSize);

        try
        {
            using (var stream = new FileStream(
                       _settings.Filename,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       bufferSize))
            {
                stream.Position = 0;
                _ = stream.Read(buffer, 0, bufferSize);

                if (!FileReaderV7.IsVersion(buffer))
                {
                    return;
                }
            }

            // run rebuild process
            Recovery(_settings.Collation);
        }
        finally
        {
            _bufferPool.Return(buffer, true);
        }
    }

    /// <summary>
    /// Upgrade old version of LiteDBX into new LiteDBX file structure. Returns true if database was completed converted
    /// If database already in current version just return false
    /// </summary>
    [Obsolete("Upgrade your LiteDBX v4 datafiles using Upgrade=true in EngineSettings. You can use upgrade=true in connection string.")]
    public static bool Upgrade(string filename, string password = null, Collation collation = null)
    {
        if (filename.IsNullOrWhiteSpace())
        {
            throw new ArgumentNullException(nameof(filename));
        }

        if (!File.Exists(filename))
        {
            return false;
        }

        var settings = new EngineSettings
        {
            Filename = filename,
            Password = password,
            Collation = collation,
            Upgrade = true
        };

        using (var db = OpenSync(settings))
        {
            // database are now converted to v5
        }

        return true;
    }
}