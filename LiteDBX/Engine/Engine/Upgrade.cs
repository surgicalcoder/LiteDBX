using System;
using System.Buffers;
using System.IO;

namespace LiteDbX.Engine;

public partial class LiteEngine
{
    private static readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;

    /// <summary>
    /// Detect and upgrade a v7 datafile to the current format before the disk service opens.
    ///
    /// Phase 6 deferred: <c>TryUpgrade()</c> is called synchronously from <c>Open()</c>
    /// (constructor path). The file detection read and the subsequent <c>Recovery()</c>
    /// call both run on the calling thread. Replacing this with an async upgrade path
    /// requires the Phase 7 async-factory constructor (<c>LiteEngine.OpenAsync()</c>).
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

        using (var stream = new FileStream(
                   _settings.Filename,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read, bufferSize))
        {
            stream.Position = 0;
            stream.Read(buffer, 0, bufferSize);

            if (!FileReaderV7.IsVersion(buffer))
            {
                return;
            }
        }

        _bufferPool.Return(buffer, true);
        // run rebuild process
        Recovery(_settings.Collation);
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

        using (var db = new LiteEngine(settings))
        {
            // database are now converted to v5
        }

        return true;
    }
}