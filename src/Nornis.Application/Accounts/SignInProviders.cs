namespace Nornis.Application.Accounts;

/// <summary>
/// The provider a sign-in comes from, read from its Auth0 subject (feature 33). The one place
/// that names providers: the subject is stored, the name is derived, so they can't disagree.
/// </summary>
public static class SignInProviders
{
    /// <summary>
    /// The provider's name for a subject such as <c>google-oauth2|1098…</c>. A custom social
    /// connection wraps its name (<c>oauth2|discord|…</c>), where Auth0's built-in one doesn't
    /// (<c>discord|…</c>); both read as Discord. An unknown connection reads as its own name.
    /// </summary>
    public static string Name(string subject)
    {
        var parts = subject.Split('|');
        var connection = parts.Length >= 3 && parts[0] == "oauth2" ? parts[1] : parts[0];

        return connection switch
        {
            "discord" => "Discord",
            "google-oauth2" => "Google",
            "windowslive" => "Microsoft",
            "apple" => "Apple",
            "github" => "GitHub",
            "auth0" => "Email",
            _ => connection,
        };
    }
}
