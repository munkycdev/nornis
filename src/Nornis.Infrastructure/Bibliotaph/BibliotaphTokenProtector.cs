using System.Security.Cryptography;
using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Application.Bibliotaph;

namespace Nornis.Infrastructure.Bibliotaph;

/// <summary>
/// Protects stored Bibliotaph refresh tokens with a Data Protection ring of their own, kept in a
/// blob that only the API and Worker identities are granted on (feature 31, design decision 4).
/// The ring is built in a private service collection, so it never mixes with a host's own Data
/// Protection keys, and the Web, whose ring is separate, can't read these tokens.
/// </summary>
public sealed class BibliotaphTokenProtector : IBibliotaphTokenProtector
{
    public const string ApplicationName = "nornis-bibliotaph";
    private const string Purpose = "Nornis.Bibliotaph.RefreshToken";
    private const string StatePurpose = "Nornis.Bibliotaph.ConnectState";

    private readonly IDataProtector _protector;
    private readonly IDataProtector _stateProtector;

    public BibliotaphTokenProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
        _stateProtector = provider.CreateProtector(StatePurpose);
    }

    /// <summary>
    /// The ring in <paramref name="keyBlobUri"/>, reached as this process's identity; with none,
    /// keys that die with the process, which is only right where nothing is configured.
    /// </summary>
    public static BibliotaphTokenProtector Create(string? keyBlobUri)
    {
        if (string.IsNullOrWhiteSpace(keyBlobUri))
        {
            return new BibliotaphTokenProtector(new EphemeralDataProtectionProvider());
        }

        var services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToAzureBlobStorage(new Uri(keyBlobUri), new DefaultAzureCredential());

        var provider = services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
        return new BibliotaphTokenProtector(provider);
    }

    public string Protect(string refreshToken) => _protector.Protect(refreshToken);

    public string ProtectState(string state) => _stateProtector.Protect(state);

    public string? UnprotectState(string protectedState) => TryUnprotect(_stateProtector, protectedState);

    public string? Unprotect(string protectedRefreshToken) => TryUnprotect(_protector, protectedRefreshToken);

    private static string? TryUnprotect(IDataProtector protector, string ciphertext)
    {
        try
        {
            return protector.Unprotect(ciphertext);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // A lost ring or tampered ciphertext. The caller treats it as a refused grant, so the
            // GM is asked to reconnect rather than every call failing with a 500.
            return null;
        }
    }
}
