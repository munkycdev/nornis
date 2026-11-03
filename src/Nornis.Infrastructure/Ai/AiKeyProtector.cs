using System.Security.Cryptography;
using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Application.Ai;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Protects users' AI keys with a Data Protection ring of their own, kept in a blob only the API
/// and Worker identities can read (feature 39, design decision 3) — the
/// <c>BibliotaphTokenProtector</c> pattern with its own application name, so neither ring can
/// read the other's secrets and the Web can read neither.
/// </summary>
public sealed class AiKeyProtector : IAiKeyProtector
{
    public const string ApplicationName = "nornis-ai-keys";
    private const string Purpose = "Nornis.AiKeys.ProviderKey";

    private readonly IDataProtector _protector;

    public AiKeyProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    /// <summary>
    /// The ring in <paramref name="keyBlobUri"/>, reached as this process's identity; with none,
    /// keys that die with the process, which is only right where nothing is configured (local
    /// development and tests: a restart then reads every saved key as rejected).
    /// </summary>
    public static AiKeyProtector Create(string? keyBlobUri)
    {
        if (string.IsNullOrWhiteSpace(keyBlobUri))
        {
            return new AiKeyProtector(new EphemeralDataProtectionProvider());
        }

        var services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToAzureBlobStorage(new Uri(keyBlobUri), new DefaultAzureCredential());

        return new AiKeyProtector(services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>());
    }

    /// <summary>
    /// The first protect creates the ring's key and writes it to the blob, so a refused write (a
    /// role that hasn't taken effect yet) surfaces here as the storage SDK's exception, which the
    /// caller can't name. It becomes <see cref="AiKeyStorageException"/>, the API's 503.
    /// </summary>
    public string Protect(string key)
    {
        try
        {
            return _protector.Protect(key);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new AiKeyStorageException(ex);
        }
    }

    public string? Unprotect(string protectedKey)
    {
        try
        {
            return _protector.Unprotect(protectedKey);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
