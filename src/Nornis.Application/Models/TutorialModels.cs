namespace Nornis.Application.Models;

/// <summary>Per-user onboarding flags (feature 20 phase C).</summary>
public record OnboardingState(bool PromptSeen, bool TutorialDismissed);

/// <summary>One tutorial step and its completion for the requesting user.</summary>
public record TutorialStepState(string Key, int Chapter, bool ClientReported, DateTimeOffset? CompletedAt);

public record TutorialChecklist(IReadOnlyList<TutorialStepState> Steps);

/// <summary>
/// The fixed tutorial step list. The step list is the product pitch in interactive form —
/// changing it is a product decision, not a refactor. Client-reported steps are page
/// visits and view toggles the server has no state for; everything else is detected from
/// actual world state and can never false-positive from clicking around.
/// </summary>
public static class TutorialSteps
{
    // Chapter 1 — playing in a world (runs in player view).
    public const string SeeAsPlayer = "see-as-player";
    public const string MeetTheCast = "meet-the-cast";
    public const string WalkTheJourney = "walk-the-journey";
    public const string StandSomewhere = "stand-somewhere";
    public const string AskTheLoremaster = "ask-the-loremaster";

    // Chapter 2 — running a campaign.
    public const string BackToGm = "back-to-gm";
    public const string AddSessionSix = "add-session-six";
    public const string WatchExtraction = "watch-extraction";
    public const string VetExtraction = "vet-extraction";
    public const string RevealSecret = "reveal-secret";
    public const string SeeWhatTheySee = "see-what-they-see";

    public sealed record Definition(string Key, int Chapter, bool ClientReported);

    public static readonly IReadOnlyList<Definition> All =
    [
        new(SeeAsPlayer, 1, ClientReported: true),
        new(MeetTheCast, 1, ClientReported: true),
        new(WalkTheJourney, 1, ClientReported: true),
        new(StandSomewhere, 1, ClientReported: true),
        new(AskTheLoremaster, 1, ClientReported: false),
        new(BackToGm, 2, ClientReported: true),
        new(AddSessionSix, 2, ClientReported: false),
        new(WatchExtraction, 2, ClientReported: false),
        new(VetExtraction, 2, ClientReported: false),
        new(RevealSecret, 2, ClientReported: false),
        new(SeeWhatTheySee, 2, ClientReported: true),
    ];
}
