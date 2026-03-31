using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
#if !NET10_0_OR_GREATER
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
#endif
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

internal class AesGcmStream : Stream, IEncryptedStream
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderMagicOffset = 1 + ENCRYPTION_SALT_SIZE;
    private const int PasswordCheckNonceOffset = HeaderMagicOffset + 8;
    private const int PasswordCheckTagOffset = PasswordCheckNonceOffset + NonceSize;
    private const int PasswordCheckCiphertextOffset = PasswordCheckTagOffset + TagSize;
    private const int PasswordCheckCiphertextSize = 32;
    private const int EncryptedPageSize = NonceSize + PAGE_SIZE + TagSize;

    private static readonly ArrayPool<byte> BufferPool = ArrayPool<byte>.Shared;
    private static readonly byte[] HeaderMagic = { (byte)'L', (byte)'D', (byte)'B', (byte)'X', (byte)'G', (byte)'C', (byte)'M', (byte)'1' };
    private static readonly byte[] PasswordCheckPlaintext = Enumerable.Repeat((byte)1, PasswordCheckCiphertextSize).ToArray();

    private readonly byte[] _key;
    private readonly string _name;
    private readonly Stream _stream;
    private long _position;

    public AesGcmStream(string password, Stream stream)
    {
        if (password == null) throw new ArgumentNullException(nameof(password));
        if (stream == null) throw new ArgumentNullException(nameof(stream));

        _stream = stream;
        _name = _stream is FileStream fileStream ? Path.GetFileName(fileStream.Name) : null;

        var isNew = _stream.Length < PAGE_SIZE;

        _stream.Position = 0;

        if (isNew)
        {
            Salt = AesStream.NewSalt();
            _key = DeriveKey(password, Salt);
            WriteNewHeader();
        }
        else
        {
            var header = BufferPool.Rent(PAGE_SIZE);

            try
            {
                ReadHeaderPage(header);

                var isEncrypted = header[0];

                if (isEncrypted != 1)
                {
                    throw LiteException.FileNotEncrypted();
                }

                if (!HasMarker(header))
                {
                    throw LiteException.FileNotEncrypted();
                }

                Salt = new byte[ENCRYPTION_SALT_SIZE];
                Buffer.BlockCopy(header, 1, Salt, 0, ENCRYPTION_SALT_SIZE);

                _key = DeriveKey(password, Salt);
                ValidatePassword(header);
            }
            catch
            {
                _stream.Dispose();
                throw;
            }
            finally
            {
                BufferPool.Return(header, true);
            }
        }

        _position = 0;
    }

    public byte[] Salt { get; }

    public AESEncryptionType EncryptionType => AESEncryptionType.GCM;

    public override bool CanRead => _stream.CanRead;

    public override bool CanSeek => _stream.CanSeek;

    public override bool CanWrite => _stream.CanWrite;

    public override long Length => GetLogicalLength(_stream.Length);

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public static bool HasMarker(Stream stream)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));

        if (!stream.CanSeek || stream.Length < PAGE_SIZE)
        {
            return false;
        }

        var position = stream.Position;
        var buffer = BufferPool.Rent(HeaderMagic.Length + 1);

        try
        {
            stream.Position = 0;

            if (ReadExact(stream, buffer, 0, buffer.Length) != buffer.Length)
            {
                return false;
            }

            if (buffer[0] != 1)
            {
                return false;
            }

            stream.Position = HeaderMagicOffset;

            if (ReadExact(stream, buffer, 0, HeaderMagic.Length) != HeaderMagic.Length)
            {
                return false;
            }

            return HeaderMagic.SequenceEqual(buffer.Take(HeaderMagic.Length));
        }
        finally
        {
            stream.Position = position;
            BufferPool.Return(buffer, true);
        }
    }

    public static long NormalizePhysicalLength(long physicalLength)
    {
        if (physicalLength <= 0)
        {
            return 0;
        }

        if (physicalLength <= PAGE_SIZE)
        {
            return PAGE_SIZE;
        }

        var dataLength = physicalLength - PAGE_SIZE;
        var pageCount = dataLength / EncryptedPageSize;

        return PAGE_SIZE + (pageCount * EncryptedPageSize);
    }

    public static long GetLogicalLength(long physicalLength)
    {
        if (physicalLength <= PAGE_SIZE)
        {
            return 0;
        }

        var dataLength = physicalLength - PAGE_SIZE;
        var pageCount = dataLength / EncryptedPageSize;

        return pageCount * PAGE_SIZE;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);

        if (count == 0 || _position >= Length)
        {
            return 0;
        }

        var totalRead = 0;
        var pageBuffer = BufferPool.Rent(PAGE_SIZE);

        try
        {
            while (count > 0 && _position < Length)
            {
                var pageIndex = _position / PAGE_SIZE;
                var pageOffset = (int)(_position % PAGE_SIZE);
                var remainingInPage = PAGE_SIZE - pageOffset;
                var bytesToCopy = Math.Min(count, remainingInPage);

                ReadPage(pageIndex, pageBuffer);
                Buffer.BlockCopy(pageBuffer, pageOffset, buffer, offset, bytesToCopy);

                offset += bytesToCopy;
                count -= bytesToCopy;
                totalRead += bytesToCopy;
                _position += bytesToCopy;
            }
        }
        finally
        {
            BufferPool.Return(pageBuffer, true);
        }

        return totalRead;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);

        if (count == 0)
        {
            return;
        }

        var pageBuffer = BufferPool.Rent(PAGE_SIZE);

        try
        {
            while (count > 0)
            {
                var pageIndex = _position / PAGE_SIZE;
                var pageOffset = (int)(_position % PAGE_SIZE);
                var remainingInPage = PAGE_SIZE - pageOffset;
                var bytesToCopy = Math.Min(count, remainingInPage);
                var isFullPageWrite = pageOffset == 0 && bytesToCopy == PAGE_SIZE;

                if (!isFullPageWrite)
                {
                    if (PageExists(pageIndex))
                    {
                        ReadPage(pageIndex, pageBuffer);
                    }
                    else
                    {
                        Array.Clear(pageBuffer, 0, PAGE_SIZE);
                    }
                }

                if (isFullPageWrite)
                {
                    Buffer.BlockCopy(buffer, offset, pageBuffer, 0, PAGE_SIZE);
                }
                else
                {
                    Buffer.BlockCopy(buffer, offset, pageBuffer, pageOffset, bytesToCopy);
                }

                WritePage(pageIndex, pageBuffer);

                offset += bytesToCopy;
                count -= bytesToCopy;
                _position += bytesToCopy;
            }
        }
        finally
        {
            BufferPool.Return(pageBuffer, true);
        }
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);

        if (count == 0 || _position >= Length)
        {
            return 0;
        }

        var totalRead = 0;
        var pageBuffer = BufferPool.Rent(PAGE_SIZE);

        try
        {
            while (count > 0 && _position < Length)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var pageIndex = _position / PAGE_SIZE;
                var pageOffset = (int)(_position % PAGE_SIZE);
                var remainingInPage = PAGE_SIZE - pageOffset;
                var bytesToCopy = Math.Min(count, remainingInPage);

                await ReadPageAsync(pageIndex, pageBuffer, cancellationToken).ConfigureAwait(false);
                Buffer.BlockCopy(pageBuffer, pageOffset, buffer, offset, bytesToCopy);

                offset += bytesToCopy;
                count -= bytesToCopy;
                totalRead += bytesToCopy;
                _position += bytesToCopy;
            }
        }
        finally
        {
            BufferPool.Return(pageBuffer, true);
        }

        return totalRead;
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);

        if (count == 0)
        {
            return;
        }

        var pageBuffer = BufferPool.Rent(PAGE_SIZE);

        try
        {
            while (count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var pageIndex = _position / PAGE_SIZE;
                var pageOffset = (int)(_position % PAGE_SIZE);
                var remainingInPage = PAGE_SIZE - pageOffset;
                var bytesToCopy = Math.Min(count, remainingInPage);
                var isFullPageWrite = pageOffset == 0 && bytesToCopy == PAGE_SIZE;

                if (!isFullPageWrite)
                {
                    if (PageExists(pageIndex))
                    {
                        await ReadPageAsync(pageIndex, pageBuffer, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        Array.Clear(pageBuffer, 0, PAGE_SIZE);
                    }
                }

                if (isFullPageWrite)
                {
                    Buffer.BlockCopy(buffer, offset, pageBuffer, 0, PAGE_SIZE);
                }
                else
                {
                    Buffer.BlockCopy(buffer, offset, pageBuffer, pageOffset, bytesToCopy);
                }

                await WritePageAsync(pageIndex, pageBuffer, cancellationToken).ConfigureAwait(false);

                offset += bytesToCopy;
                count -= bytesToCopy;
                _position += bytesToCopy;
            }
        }
        finally
        {
            BufferPool.Return(pageBuffer, true);
        }
    }

    public override void Flush()
    {
        _stream.Flush();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var reference = origin switch
        {
            SeekOrigin.Begin => 0,
            SeekOrigin.Current => _position,
            SeekOrigin.End => Length,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null)
        };

        var newPosition = reference + offset;

        if (newPosition < 0)
        {
            throw new IOException("Attempted to seek before the beginning of the stream.");
        }

        _position = newPosition;

        return _position;
    }

    public override void SetLength(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        var pageCount = value / PAGE_SIZE;
        var physicalLength = PAGE_SIZE + (pageCount * EncryptedPageSize);

        _stream.SetLength(physicalLength);

        if (_position > value)
        {
            _position = value;
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _stream.Dispose();
        }
    }

    private void WriteNewHeader()
    {
        var header = new byte[PAGE_SIZE];
        var passwordCheckNonce = new byte[NonceSize];
        var passwordCheckCiphertext = new byte[PasswordCheckCiphertextSize];
        var passwordCheckTag = new byte[TagSize];

        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(passwordCheckNonce);
        }
        Encrypt(PasswordCheckPlaintext, passwordCheckNonce, HeaderMagic, passwordCheckCiphertext, passwordCheckTag);

        header[0] = 1;
        Buffer.BlockCopy(Salt, 0, header, 1, ENCRYPTION_SALT_SIZE);
        Buffer.BlockCopy(HeaderMagic, 0, header, HeaderMagicOffset, HeaderMagic.Length);
        Buffer.BlockCopy(passwordCheckNonce, 0, header, PasswordCheckNonceOffset, NonceSize);
        Buffer.BlockCopy(passwordCheckTag, 0, header, PasswordCheckTagOffset, TagSize);
        Buffer.BlockCopy(passwordCheckCiphertext, 0, header, PasswordCheckCiphertextOffset, PasswordCheckCiphertextSize);

        _stream.Position = 0;
        _stream.Write(header, 0, header.Length);
        _stream.FlushToDisk();
    }

    private void ValidatePassword(byte[] header)
    {
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var ciphertext = new byte[PasswordCheckCiphertextSize];
        var plaintext = new byte[PasswordCheckCiphertextSize];

        Buffer.BlockCopy(header, PasswordCheckNonceOffset, nonce, 0, nonce.Length);
        Buffer.BlockCopy(header, PasswordCheckTagOffset, tag, 0, tag.Length);
        Buffer.BlockCopy(header, PasswordCheckCiphertextOffset, ciphertext, 0, ciphertext.Length);

        try
        {
            Decrypt(ciphertext, nonce, HeaderMagic, tag, plaintext);
        }
        catch (Exception ex) when (IsAuthenticationException(ex))
        {
            throw LiteException.InvalidPassword();
        }

        if (!plaintext.SequenceEqual(PasswordCheckPlaintext))
        {
            throw LiteException.InvalidPassword();
        }
    }

    private void ReadHeaderPage(byte[] header)
    {
        _stream.Position = 0;

        if (ReadExact(_stream, header, 0, PAGE_SIZE) != PAGE_SIZE)
        {
            throw LiteException.FileNotEncrypted();
        }
    }

    private bool PageExists(long pageIndex)
    {
        var recordOffset = GetRecordOffset(pageIndex);
        return recordOffset + EncryptedPageSize <= _stream.Length;
    }

    private void ReadPage(long pageIndex, byte[] destination)
    {
        var recordOffset = GetRecordOffset(pageIndex);

        if (recordOffset + EncryptedPageSize > _stream.Length)
        {
            Array.Clear(destination, 0, PAGE_SIZE);
            return;
        }

        var nonce = new byte[NonceSize];
        var ciphertext = new byte[PAGE_SIZE];
        var tag = new byte[TagSize];

        ReadExactAt(recordOffset, nonce, 0, NonceSize);
        ReadExactAt(recordOffset + NonceSize, ciphertext, 0, PAGE_SIZE);
        ReadExactAt(recordOffset + NonceSize + PAGE_SIZE, tag, 0, TagSize);

        try
        {
            Decrypt(ciphertext, nonce, GetAdditionalData(pageIndex), tag, destination);
        }
        catch (Exception ex) when (IsAuthenticationException(ex))
        {
            throw LiteException.InvalidPassword();
        }
    }

    private async Task ReadPageAsync(long pageIndex, byte[] destination, CancellationToken cancellationToken)
    {
        var recordOffset = GetRecordOffset(pageIndex);

        if (recordOffset + EncryptedPageSize > _stream.Length)
        {
            Array.Clear(destination, 0, PAGE_SIZE);
            return;
        }

        var nonce = new byte[NonceSize];
        var ciphertext = new byte[PAGE_SIZE];
        var tag = new byte[TagSize];

        await ReadExactAtAsync(recordOffset, nonce, 0, NonceSize, cancellationToken).ConfigureAwait(false);
        await ReadExactAtAsync(recordOffset + NonceSize, ciphertext, 0, PAGE_SIZE, cancellationToken).ConfigureAwait(false);
        await ReadExactAtAsync(recordOffset + NonceSize + PAGE_SIZE, tag, 0, TagSize, cancellationToken).ConfigureAwait(false);

        try
        {
            Decrypt(ciphertext, nonce, GetAdditionalData(pageIndex), tag, destination);
        }
        catch (Exception ex) when (IsAuthenticationException(ex))
        {
            throw LiteException.InvalidPassword();
        }
    }

    private void WritePage(long pageIndex, byte[] source)
    {
        var recordOffset = GetRecordOffset(pageIndex);
        var nonce = new byte[NonceSize];
        var ciphertext = new byte[PAGE_SIZE];
        var tag = new byte[TagSize];

        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(nonce);
        }
        Encrypt(source, nonce, GetAdditionalData(pageIndex), ciphertext, tag);

        WriteAt(recordOffset, nonce, 0, nonce.Length);
        WriteAt(recordOffset + NonceSize, ciphertext, 0, ciphertext.Length);
        WriteAt(recordOffset + NonceSize + PAGE_SIZE, tag, 0, tag.Length);
    }

    private async Task WritePageAsync(long pageIndex, byte[] source, CancellationToken cancellationToken)
    {
        var recordOffset = GetRecordOffset(pageIndex);
        var nonce = new byte[NonceSize];
        var ciphertext = new byte[PAGE_SIZE];
        var tag = new byte[TagSize];

        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(nonce);
        }
        Encrypt(source, nonce, GetAdditionalData(pageIndex), ciphertext, tag);

        await WriteAtAsync(recordOffset, nonce, 0, nonce.Length, cancellationToken).ConfigureAwait(false);
        await WriteAtAsync(recordOffset + NonceSize, ciphertext, 0, ciphertext.Length, cancellationToken).ConfigureAwait(false);
        await WriteAtAsync(recordOffset + NonceSize + PAGE_SIZE, tag, 0, tag.Length, cancellationToken).ConfigureAwait(false);
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        using var pdb = new Rfc2898DeriveBytes(password, salt);
        return pdb.GetBytes(32);
    }

    private void Encrypt(byte[] plaintext, byte[] nonce, byte[] associatedData, byte[] ciphertext, byte[] tag)
    {
#if NET10_0_OR_GREATER
        using var aesGcm = new AesGcm(_key, TagSize);
        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
#else
        var cipher = new GcmBlockCipher(new AesEngine());
        var parameters = new AeadParameters(new KeyParameter(_key), TagSize * 8, nonce, associatedData);
        var output = new byte[plaintext.Length + TagSize];

        cipher.Init(true, parameters);

        var length = cipher.ProcessBytes(plaintext, 0, plaintext.Length, output, 0);
        length += cipher.DoFinal(output, length);

        Buffer.BlockCopy(output, 0, ciphertext, 0, plaintext.Length);
        Buffer.BlockCopy(output, plaintext.Length, tag, 0, TagSize);
#endif
    }

    private void Decrypt(byte[] ciphertext, byte[] nonce, byte[] associatedData, byte[] tag, byte[] plaintext)
    {
#if NET10_0_OR_GREATER
        using var aesGcm = new AesGcm(_key, TagSize);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
#else
        var cipher = new GcmBlockCipher(new AesEngine());
        var parameters = new AeadParameters(new KeyParameter(_key), TagSize * 8, nonce, associatedData);
        var input = new byte[ciphertext.Length + tag.Length];

        Buffer.BlockCopy(ciphertext, 0, input, 0, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, input, ciphertext.Length, tag.Length);

        cipher.Init(false, parameters);

        var length = cipher.ProcessBytes(input, 0, input.Length, plaintext, 0);
        length += cipher.DoFinal(plaintext, length);

        if (length != plaintext.Length)
        {
            throw new CryptographicException("Unexpected plaintext length when decrypting GCM page.");
        }
#endif
    }

    private static bool IsAuthenticationException(Exception ex)
    {
#if NET10_0_OR_GREATER
        return ex is CryptographicException;
#else
        return ex is CryptographicException || ex is InvalidCipherTextException;
#endif
    }

    private static bool HasMarker(byte[] header)
    {
        if (header[0] != 1)
        {
            return false;
        }

        for (var i = 0; i < HeaderMagic.Length; i++)
        {
            if (header[HeaderMagicOffset + i] != HeaderMagic[i])
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] GetAdditionalData(long pageIndex)
    {
        var aad = new byte[HeaderMagic.Length + sizeof(long)];

        Buffer.BlockCopy(HeaderMagic, 0, aad, 0, HeaderMagic.Length);
        Buffer.BlockCopy(BitConverter.GetBytes(pageIndex), 0, aad, HeaderMagic.Length, sizeof(long));

        return aad;
    }

    private static int ReadExact(Stream stream, byte[] buffer, int offset, int count)
    {
        var total = 0;

        while (count > 0)
        {
            var read = stream.Read(buffer, offset, count);

            if (read == 0)
            {
                break;
            }

            total += read;
            offset += read;
            count -= read;
        }

        return total;
    }

    private void ReadExactAt(long position, byte[] buffer, int offset, int count)
    {
        _stream.Position = position;

        if (ReadExact(_stream, buffer, offset, count) != count)
        {
            throw new EndOfStreamException($"Unexpected end of encrypted stream while reading page data. File={_name}");
        }
    }

    private async Task ReadExactAtAsync(long position, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        _stream.Position = position;

        var remaining = count;

        while (remaining > 0)
        {
            var read = await _stream.ReadAsync(buffer, offset, remaining, cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                throw new EndOfStreamException($"Unexpected end of encrypted stream while reading page data. File={_name}");
            }

            offset += read;
            remaining -= read;
        }
    }

    private void WriteAt(long position, byte[] buffer, int offset, int count)
    {
        _stream.Position = position;
        _stream.Write(buffer, offset, count);
    }

    private async Task WriteAtAsync(long position, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        _stream.Position = position;
        await _stream.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateBufferArguments(byte[] buffer, int offset, int count)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));

        if (offset < 0 || count < 0 || offset + count > buffer.Length)
        {
            throw new ArgumentOutOfRangeException();
        }
    }

    private static long GetRecordOffset(long pageIndex)
    {
        return PAGE_SIZE + (pageIndex * EncryptedPageSize);
    }
}

