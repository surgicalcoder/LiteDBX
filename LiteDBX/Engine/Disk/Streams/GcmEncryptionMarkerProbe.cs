using System;
using System.Buffers;
using System.IO;
using System.Linq;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

internal static class GcmEncryptionMarkerProbe
{
    private static readonly ArrayPool<byte> BufferPool = ArrayPool<byte>.Shared;
    private static readonly byte[] HeaderMagic = { (byte)'L', (byte)'D', (byte)'B', (byte)'X', (byte)'G', (byte)'C', (byte)'M', (byte)'1' };
    private const int HeaderMagicOffset = 1 + ENCRYPTION_SALT_SIZE;

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
}

