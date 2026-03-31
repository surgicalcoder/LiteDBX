using System.IO;

namespace LiteDbX.Engine;

/// <summary>
/// Contract for optional encryption providers that can be plugged into LiteDbX.
/// ECB remains built-in; additional modes (for example GCM) can be registered explicitly.
/// </summary>
public interface IEncryptionProvider
{
    /// <summary>
    /// Encryption mode implemented by this provider.
    /// </summary>
    AESEncryptionType EncryptionType { get; }

    /// <summary>
    /// Returns true when the supplied stream already contains data for this provider's format.
    /// Implementations must restore the incoming stream position before returning.
    /// </summary>
    bool IsMatch(Stream stream);

    /// <summary>
    /// Open an encrypted stream using this provider.
    /// </summary>
    Stream Open(string password, Stream stream);

    /// <summary>
    /// Gets the logical length of the encrypted payload, normalizing any incomplete trailing
    /// provider-specific records when possible.
    /// </summary>
    long GetLogicalLength(Stream stream);
}

