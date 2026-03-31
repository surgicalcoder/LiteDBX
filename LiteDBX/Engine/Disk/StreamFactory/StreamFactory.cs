using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

/// <summary>
/// Simple Stream disk implementation of disk factory - used for Memory/Temp database
/// [ThreadSafe]
///
/// Phase 3: the shared <see cref="SemaphoreSlim"/> gate is passed to every
/// <see cref="ConcurrentStream"/> so that concurrent async readers are mutually exclusive.
/// </summary>
internal class StreamFactory : IStreamFactory
{
    private readonly AESEncryptionType _aesEncryption;
    private readonly string _password;
    private readonly SemaphoreSlim _streamGate = new SemaphoreSlim(1, 1);
    private readonly Stream _stream;

    public StreamFactory(Stream stream, string password, AESEncryptionType aesEncryption = AESEncryptionType.ECB)
    {
        _stream = stream;
        _password = password;
        _aesEncryption = aesEncryption;
    }

    /// <summary>
    /// Stream has no name (use stream type)
    /// </summary>
    public string Name => _stream is MemoryStream ? ":memory:" : _stream is TempStream ? ":temp:" : ":stream:";

    /// <summary>
    /// Use ConcurrentStream wrapper to support multi thread in same Stream (using semaphore gate)
    /// </summary>
    public Stream GetStream(bool canWrite, bool sequencial)
    {
        if (_password == null)
        {
            return new ConcurrentStream(_stream, canWrite, _streamGate);
        }

        return EncryptedStreamFactory.Open(_password, new ConcurrentStream(_stream, canWrite, _streamGate), _aesEncryption);
    }

    /// <summary>
    /// Async-compatible stream acquisition.
    /// Stream construction is synchronous (MemoryStream/TempStream have no async constructor),
    /// so this method completes synchronously while still honouring the async-first interface.
    /// </summary>
    public ValueTask<Stream> GetStreamAsync(bool canWrite, bool sequential, CancellationToken cancellationToken = default)
    {
        return new ValueTask<Stream>(GetStream(canWrite, sequential));
    }

    /// <summary>
    /// Get file length using _stream.Length
    /// </summary>
    public long GetLength()
    {
        return EncryptedStreamFactory.GetLogicalLength(_stream, _password);
    }

    /// <summary>
    /// Check if file exists based on stream length
    /// </summary>
    public bool Exists()
    {
        return _stream.Length > 0;
    }

    /// <summary>
    /// There is no delete method in Stream factory
    /// </summary>
    public void Delete() { }

    /// <summary>
    /// Test if this file are locked by another process (there is no way to test when Stream only)
    /// </summary>
    public bool IsLocked()
    {
        return false;
    }

    /// <summary>
    /// Do no dispose on finish
    /// </summary>
    public bool CloseOnDispose => false;
}