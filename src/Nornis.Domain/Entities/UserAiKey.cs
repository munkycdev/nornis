using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

/// <summary>
/// A user's own key for one AI provider (feature 39). A world's AI calls spend its creator's
/// keys; a user has at most one key per provider. The key itself is stored only as Data
/// Protection ciphertext, readable by the API and the Worker, never by the Web.
/// </summary>
public class UserAiKey
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public AiProvider Provider { get; set; }

    /// <summary>The user's name for the key, shown where a cost says who paid.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>The key, as <c>AiKeyProtector</c> ciphertext. Never sent to any client.</summary>
    public string ProtectedKey { get; set; } = string.Empty;

    /// <summary>The key's last four characters, so the user can tell which key this is.</summary>
    public string LastFour { get; set; } = string.Empty;

    /// <summary>The endpoint, for <see cref="AiProvider.OpenAiCompatible"/> only; null otherwise.</summary>
    public string? BaseUrl { get; set; }

    public DateTimeOffset AddedAt { get; set; }

    /// <summary>When the key was last saved. A replaced key changes this, which retires any
    /// client built with the old one.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset CheckedAt { get; set; }

    /// <summary>The last check, or the last call made with the key, was refused by the provider.
    /// A failed key is never spent again until it is saved anew.</summary>
    public bool CheckFailed { get; set; }

    public List<UserAiCompatibleModel> CompatibleModels { get; set; } = [];
}
