using System;
using System.IO;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

internal static class EncryptedStreamFactory
{
    public static Stream Open(string password, Stream stream, AESEncryptionType aesEncryption)
    {
        if (stream.Length >= PAGE_SIZE)
        {
            if (GcmEncryptionMarkerProbe.HasMarker(stream))
            {
                if (!EncryptionProviderRegistry.TryGet(AESEncryptionType.GCM, out var gcmProvider))
                {
                    throw LiteException.EncryptionProviderNotRegistered(AESEncryptionType.GCM);
                }

                return gcmProvider.Open(password, stream);
            }

            foreach (var provider in EncryptionProviderRegistry.GetRegisteredProviders())
            {
                if (provider.IsMatch(stream))
                {
                    return provider.Open(password, stream);
                }
            }

            return new AesStream(password, stream);
        }

        if (aesEncryption == AESEncryptionType.ECB)
        {
            return new AesStream(password, stream);
        }

        if (!EncryptionProviderRegistry.TryGet(aesEncryption, out var selectedProvider))
        {
            throw LiteException.EncryptionProviderNotRegistered(aesEncryption);
        }

        return selectedProvider.Open(password, stream);
    }

    public static long GetLogicalLength(Stream stream, string password)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));

        var length = stream.Length;

        if (password == null)
        {
            return NormalizePlainLength(stream, length);
        }

        if (length >= PAGE_SIZE)
        {
            if (GcmEncryptionMarkerProbe.HasMarker(stream))
            {
                if (!EncryptionProviderRegistry.TryGet(AESEncryptionType.GCM, out var gcmProvider))
                {
                    throw LiteException.EncryptionProviderNotRegistered(AESEncryptionType.GCM);
                }

                return gcmProvider.GetLogicalLength(stream);
            }

            foreach (var provider in EncryptionProviderRegistry.GetRegisteredProviders())
            {
                if (provider.IsMatch(stream))
                {
                    return provider.GetLogicalLength(stream);
                }
            }
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
}

