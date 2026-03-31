using System.IO;
using LiteDbX.Engine;

namespace LiteDbX.Encryption.Gcm;

/// <summary>
/// Optional AES-GCM encryption provider for LiteDbX.
/// </summary>
public sealed class GcmEncryptionProvider : IEncryptionProvider
{
    public AESEncryptionType EncryptionType => AESEncryptionType.GCM;

    public bool IsMatch(Stream stream)
    {
        return AesGcmStream.HasMarker(stream);
    }

    public Stream Open(string password, Stream stream)
    {
        return new AesGcmStream(password, stream);
    }

    public long GetLogicalLength(Stream stream)
    {
        var normalizedLength = AesGcmStream.NormalizePhysicalLength(stream.Length);

        if (normalizedLength != stream.Length && stream.CanWrite)
        {
            stream.SetLength(normalizedLength);
            FlushToDisk(stream);
        }

        return AesGcmStream.GetLogicalLength(normalizedLength);
    }

    private static void FlushToDisk(Stream stream)
    {
        if (stream is FileStream fileStream)
        {
            fileStream.Flush(true);
        }
        else
        {
            stream.Flush();
        }
    }
}

