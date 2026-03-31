using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace LiteDbX.Engine;

/// <summary>
/// Registry for optional encryption providers.
/// Consumers that need non-core encryption modes should reference the add-on package and register
/// its provider before opening databases that use that mode.
/// </summary>
public static class EncryptionProviderRegistry
{
    private static readonly ConcurrentDictionary<AESEncryptionType, IEncryptionProvider> Providers =
        new ConcurrentDictionary<AESEncryptionType, IEncryptionProvider>();

    /// <summary>
    /// Register or replace a provider for the provider's encryption mode.
    /// </summary>
    public static void Register(IEncryptionProvider provider)
    {
        if (provider == null) throw new ArgumentNullException(nameof(provider));

        if (provider.EncryptionType == AESEncryptionType.ECB)
        {
            throw new ArgumentException("ECB is built into LiteDbX and must not be registered as an external provider.", nameof(provider));
        }

        Providers[provider.EncryptionType] = provider;
    }

    /// <summary>
    /// Returns true when a provider for the requested mode has been registered.
    /// ECB always returns true because it is built into LiteDbX.
    /// </summary>
    public static bool IsRegistered(AESEncryptionType encryptionType)
    {
        return encryptionType == AESEncryptionType.ECB || Providers.ContainsKey(encryptionType);
    }

    internal static bool TryGet(AESEncryptionType encryptionType, out IEncryptionProvider provider)
    {
        return Providers.TryGetValue(encryptionType, out provider);
    }

    internal static IReadOnlyList<IEncryptionProvider> GetRegisteredProviders()
    {
        return Providers.Values.OrderBy(x => x.EncryptionType).ToArray();
    }
}

