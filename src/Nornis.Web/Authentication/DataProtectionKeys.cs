using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;

namespace Nornis.Web.Authentication;

/// <summary>
/// Where the keys that protect the sign-in cookie, antiforgery tokens and circuit state live.
///
/// The framework default keeps them on the container's filesystem, which a deploy replaces:
/// every rollout minted a fresh key ring, so every signed-in member was signed out and any
/// form open at the time failed its antiforgery check. The ring now lives in a blob the Web
/// app reaches as its own managed identity, so it outlives revisions.
///
/// The keys are not wrapped by a second key. The container is private, shared-key access is off
/// on the account, and the Web identity is the only principal granted on it.
/// </summary>
public static class DataProtectionKeys
{
    public const string KeyBlobUriKey = "DataProtection:KeyBlobUri";

    /// <param name="signInEnabled">
    /// Whether the app issues sign-in cookies. Without sign-in (local dev) an unset blob means
    /// keys that die with the process, which costs nothing. With sign-in, an unset blob is a
    /// misconfiguration whose only symptom is everyone being signed out on the next deploy, so
    /// it refuses to start instead.
    /// </param>
    public static IServiceCollection AddNornisDataProtection(
        this IServiceCollection services, IConfiguration configuration, bool signInEnabled)
    {
        var dataProtection = services.AddDataProtection().SetApplicationName("nornis-web");

        var keyBlobUri = configuration[KeyBlobUriKey];
        if (!string.IsNullOrWhiteSpace(keyBlobUri))
        {
            dataProtection.PersistKeysToAzureBlobStorage(new Uri(keyBlobUri), new DefaultAzureCredential());
        }
        else if (signInEnabled)
        {
            throw new InvalidOperationException(
                $"Sign-in is enabled but {KeyBlobUriKey} is not set. Without it the cookie keys are " +
                "regenerated on every deploy and every member is signed out.");
        }

        return services;
    }
}
