using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

/// <summary>
/// Encrypted AES Stream
///
/// Phase 3: <see cref="ReadAsync(byte[],int,int,CancellationToken)"/> and
/// <see cref="WriteAsync(byte[],int,int,CancellationToken)"/> now delegate to the underlying
/// <see cref="CryptoStream"/> async paths rather than silently falling back to the inherited
/// synchronous default.  The underlying <see cref="FileStream"/> must be opened with
/// <c>FileOptions.Asynchronous</c> (guaranteed by <see cref="FileStreamFactory"/>) for these
/// calls to issue genuine OS-level async I/O.
///
/// Durability note: <c>CryptoStream.FlushAsync</c> flushes the crypto buffer to the underlying
/// stream; physical-disk durability requires the underlying <c>FileStream.Flush(true)</c> which is
/// still synchronous.  This is acceptable on shutdown/checkpoint paths but must not be called
/// on hot async write paths without careful consideration.
/// </summary>
public class AesStream : Stream, IEncryptedStream
{
    private static readonly byte[] _emptyContent = new byte[PAGE_SIZE - 1 - 16]; // 1 for aes indicator + 16 for salt

    private static readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;
    private readonly Aes _aes;

    private readonly byte[] _decryptedZeroes = new byte[16];
    private readonly ICryptoTransform _decryptor;
    private readonly ICryptoTransform _encryptor;

    private readonly string _name;
    private readonly CryptoStream _reader;
    private readonly Stream _stream;
    private readonly CryptoStream _writer;

    public AesStream(string password, Stream stream, AESEncryptionType aesEncryption = AESEncryptionType.ECB)
    {
        if (aesEncryption == AESEncryptionType.GCM)
        {
            throw new NotSupportedException("Use AesGcmStream for AES-GCM encrypted streams.");
        }

        _stream = stream;
        _name = _stream is FileStream fileStream ? Path.GetFileName(fileStream.Name) : null;

        var isNew = _stream.Length < PAGE_SIZE;

        // start stream from zero position
        _stream.Position = 0;

        const int checkBufferSize = 32;

        var checkBuffer = _bufferPool.Rent(checkBufferSize);
        var msBuffer = _bufferPool.Rent(16);

        try
        {
            // new file? create new salt
            if (isNew)
            {
                Salt = NewSalt();

                // first byte =1 means this datafile is encrypted
                _stream.WriteByte(1);
                _stream.Write(Salt, 0, ENCRYPTION_SALT_SIZE);
            }
            else
            {
                Salt = new byte[ENCRYPTION_SALT_SIZE];

                // checks if this datafile are encrypted
                var isEncrypted = _stream.ReadByte();

                if (isEncrypted != 1)
                {
                    throw LiteException.FileNotEncrypted();
                }

                _stream.Read(Salt, 0, ENCRYPTION_SALT_SIZE);
            }

            _aes = Aes.Create();
            _aes.Padding = PaddingMode.None;
            _aes.Mode = CipherMode.ECB;

            var pdb = new Rfc2898DeriveBytes(password, Salt);

            using (pdb)
            {
                _aes.Key = pdb.GetBytes(32);
                _aes.IV = pdb.GetBytes(16);
            }

            _encryptor = _aes.CreateEncryptor();
            _decryptor = _aes.CreateDecryptor();

            _reader = _stream.CanRead ? new CryptoStream(_stream, _decryptor, CryptoStreamMode.Read) : null;

            _writer = _stream.CanWrite ? new CryptoStream(_stream, _encryptor, CryptoStreamMode.Write) : null;

            // set stream to password checking
            _stream.Position = 32;


            if (!isNew)
            {
                // check whether bytes 32 to 64 is empty. This indicates LiteDb was unable to write encrypted 1s during last attempt.
                _stream.Read(checkBuffer, 0, checkBufferSize);
                isNew = checkBuffer.All(x => x == 0);

                // reset checkBuffer and stream position
                Array.Clear(checkBuffer, 0, checkBufferSize);
                _stream.Position = 32;
            }

            // fill checkBuffer with encrypted 1 to check when open
            if (isNew)
            {
                checkBuffer.Fill(1, 0, checkBufferSize);

                _writer.Write(checkBuffer, 0, checkBufferSize);

                //ensure that the "hidden" page in encrypted files is created correctly
                _stream.Position = PAGE_SIZE - 1;
                _stream.WriteByte(0);
            }
            else
            {
                _reader.Read(checkBuffer, 0, checkBufferSize);

                if (!checkBuffer.All(x => x == 1))
                {
                    throw LiteException.InvalidPassword();
                }
            }

            _stream.Position = PAGE_SIZE;
            _stream.FlushToDisk();

            using (var ms = new MemoryStream(msBuffer))
            {
                using (var tempStream = new CryptoStream(ms, _decryptor, CryptoStreamMode.Read))
                {
                    tempStream.Read(_decryptedZeroes, 0, _decryptedZeroes.Length);
                }
            }
        }
        catch
        {
            _stream.Dispose();

            throw;
        }
        finally
        {
            _bufferPool.Return(msBuffer, true);
            _bufferPool.Return(checkBuffer, true);
        }
    }

    public byte[] Salt { get; }

    public AESEncryptionType EncryptionType => AESEncryptionType.ECB;

    public override bool CanRead => _stream.CanRead;

    public override bool CanSeek => _stream.CanSeek;

    public override bool CanWrite => _stream.CanWrite;

    public override long Length => _stream.Length - PAGE_SIZE;

    public override long Position
    {
        get => _stream.Position - PAGE_SIZE;
        set => Seek(value, SeekOrigin.Begin);
    }

    public long StreamPosition => _stream.Position;

    /// <summary>
    /// Decrypt data from Stream
    /// </summary>
    public override int Read(byte[] array, int offset, int count)
    {
        ENSURE(Position % PAGE_SIZE == 0, "AesRead: position must be in PAGE_SIZE module. Position={0}, File={1}", Position, _name);

        var r = _reader.Read(array, offset, count);

        // checks if the first 16 bytes of the page in the original stream are zero
        // this should never happen, but if it does, return a blank page
        // the blank page will be skipped by WalIndexService.CheckpointInternal() and WalIndexService.RestoreIndex()
        if (IsBlank(array, offset))
        {
            array.Fill(0, offset, count);
        }

        return r;
    }

    /// <summary>
    /// Encrypt data to Stream
    /// </summary>
    public override void Write(byte[] array, int offset, int count)
    {
        ENSURE(count == PAGE_SIZE || count == 1, "buffer size must be PAGE_SIZE");
        ENSURE(Position == HeaderPage.P_INVALID_DATAFILE_STATE || Position % PAGE_SIZE == 0, "AesWrite: position must be in PAGE_SIZE module. Position={0}, File={1}", Position, _name);

        _writer.Write(array, offset, count);
    }

    /// <summary>
    /// Async decrypt data from Stream.
    /// Delegates to <see cref="CryptoStream.ReadAsync"/> which on .NET Core 2.1+ issues genuine
    /// async I/O through the underlying stream.
    /// </summary>
    public override async Task<int> ReadAsync(byte[] array, int offset, int count, CancellationToken cancellationToken)
    {
        ENSURE(Position % PAGE_SIZE == 0, "AesRead: position must be in PAGE_SIZE module. Position={0}, File={1}", Position, _name);

        var r = await _reader.ReadAsync(array, offset, count, cancellationToken).ConfigureAwait(false);

        if (IsBlank(array, offset))
        {
            array.Fill(0, offset, count);
        }

        return r;
    }

    /// <summary>
    /// Async encrypt data to Stream.
    /// Delegates to <see cref="CryptoStream.WriteAsync"/> which on .NET Core 2.1+ issues genuine
    /// async I/O through the underlying stream.
    /// </summary>
    public override Task WriteAsync(byte[] array, int offset, int count, CancellationToken cancellationToken)
    {
        ENSURE(count == PAGE_SIZE || count == 1, "buffer size must be PAGE_SIZE");
        ENSURE(Position == HeaderPage.P_INVALID_DATAFILE_STATE || Position % PAGE_SIZE == 0, "AesWrite: position must be in PAGE_SIZE module. Position={0}, File={1}", Position, _name);

        return _writer.WriteAsync(array, offset, count, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _stream?.Dispose();

        _encryptor.Dispose();
        _decryptor.Dispose();

        _aes.Dispose();
    }

    /// <summary>
    /// Get new salt for encryption
    /// </summary>
    public static byte[] NewSalt()
    {
        var salt = new byte[ENCRYPTION_SALT_SIZE];

        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }

        return salt;
    }

    public override void Flush()
    {
        _stream.Flush();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        return _stream.Seek(offset + PAGE_SIZE, origin);
    }

    public override void SetLength(long value)
    {
        _stream.SetLength(value + PAGE_SIZE);
    }

    private unsafe bool IsBlank(byte[] array, int offset)
    {
        fixed (byte* arrayPtr = array)
        fixed (void* vPtr = _decryptedZeroes)
        {
            var ptr = (ulong*)(arrayPtr + offset);
            var zeroptr = (ulong*)vPtr;

            return *ptr == *zeroptr && *(ptr + 1) == *(zeroptr + 1);
        }
    }
}