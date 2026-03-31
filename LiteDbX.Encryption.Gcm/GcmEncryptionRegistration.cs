using LiteDbX.Engine;

namespace LiteDbX.Encryption.Gcm;

/// <summary>
/// Explicit registration entry point for the optional AES-GCM provider.
/// </summary>
public static class GcmEncryptionRegistration
{
    private static readonly GcmEncryptionProvider Provider = new GcmEncryptionProvider();

    public static void Register()
    {
        EncryptionProviderRegistry.Register(Provider);
    }
}

