using System;
using System.IO;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

internal static class EncryptedStreamFactory
{
    public static Stream Open(string password, Stream stream, AESEncryptionType aesEncryption)
    {
        if (stream.Length >= PAGE_SIZE && AesGcmStream.HasMarker(stream))
        {
            return new AesGcmStream(password, stream);
        }

        if (stream.Length < PAGE_SIZE && aesEncryption == AESEncryptionType.GCM)
        {
            return new AesGcmStream(password, stream);
        }

        return new AesStream(password, stream);
    }

    public static long GetLogicalLength(Stream stream, string password)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));

        var length = stream.Length;

        if (password == null)
        {
            return NormalizePlainLength(stream, length);
        }

        if (length >= PAGE_SIZE && AesGcmStream.HasMarker(stream))
        {
            return NormalizeGcmLength(stream, length);
        }

        return NormalizeEcbLength(stream, length);
    }

    private static long NormalizePlainLength(Stream stream, long length)
    {
        var normalizedLength = length - length % PAGE_SIZE;

        if (normalizedLength != length && stream.CanWrite)
        {
            stream.SetLength(normalizedLength);
            stream.FlushToDisk();
        }

        return normalizedLength;
    }

    private static long NormalizeEcbLength(Stream stream, long length)
    {
        var normalizedLength = length - length % PAGE_SIZE;

        if (normalizedLength != length && stream.CanWrite)
        {
            stream.SetLength(normalizedLength);
            stream.FlushToDisk();
        }

        return normalizedLength > 0 ? normalizedLength - PAGE_SIZE : 0;
    }

    private static long NormalizeGcmLength(Stream stream, long length)
    {
        var normalizedLength = AesGcmStream.NormalizePhysicalLength(length);

        if (normalizedLength != length && stream.CanWrite)
        {
            stream.SetLength(normalizedLength);
            stream.FlushToDisk();
        }

        return AesGcmStream.GetLogicalLength(normalizedLength);
    }
}

