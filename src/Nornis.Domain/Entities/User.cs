namespace Nornis.Domain.Entities;

public class User
{
    public Guid Id { get; set; }

    public string Auth0SubjectId { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// When the first-login onboarding prompt was shown (or dismissed). Set once, checked
    /// on home: null means the user has never seen the prompt. It never reappears after.
    /// </summary>
    public DateTimeOffset? OnboardingPromptSeenAt { get; set; }

    /// <summary>
    /// When the user cancelled the tutorial outright. Permanent: no tutorial UI is shown
    /// again for this user, on any world, ever (feature 20, Requirement 4.4).
    /// </summary>
    public DateTimeOffset? TutorialDismissedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];
}
