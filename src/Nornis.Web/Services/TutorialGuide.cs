using Nornis.Web.Navigation;

namespace Nornis.Web.Services;

/// <summary>What the client watches for to check a step off. Server-detected steps are State.</summary>
public enum TutorialTrigger
{
    /// <summary>Detected from world state on the server; the client only shows it.</summary>
    State,

    /// <summary>Arriving on the step's page, or anything under it.</summary>
    Visit,

    /// <summary>Turning player view on.</summary>
    EnterPlayerView,

    /// <summary>Turning player view off again.</summary>
    LeavePlayerView,

    /// <summary>Going somewhere through the quick switcher.</summary>
    Jump,

    /// <summary>Opening the step's page while viewing as player, once there is a reveal to find there.</summary>
    LearnedAsPlayer,
}

/// <summary>
/// One tutorial step as the client tells it. <paramref name="Hint"/> may name pages by route in
/// braces — <c>{/sources}</c> — which <see cref="TutorialGuide.HintFor"/> expands to the sidebar's
/// current label for that page. <paramref name="InPlayerView"/> marks the steps a person does
/// while viewing as player, whose pages must therefore be ones a player can open.
/// </summary>
public sealed record TutorialStep(
    string Key,
    string Title,
    string? Href,
    string Hint,
    TutorialTrigger Trigger,
    bool InPlayerView = false);

/// <summary>
/// The client's half of the demo-world tutorial: what each step is called, where it is done, how
/// it reads, and what the client watches for. Chapters and completion arrive from the server, and
/// the keys mirror <c>TutorialSteps</c> in Nornis.Application — a legitimate mirror, since Web and
/// Api deploy separately and share no assembly. A key the server sends that is not here renders
/// by its key rather than failing, so the two halves may roll out in either order.
///
/// Where a step is done is never written into its hint. It is looked up in <see cref="NavGroups"/>
/// when rendered, so a step follows its page when the sidebar is regrouped or relabelled: on
/// 2026-09-10 the checklist still read "Click Locations in the sidebar" a day after that entry
/// became Map under World, because the hints were prose copies of a table that had moved.
/// </summary>
public static class TutorialGuide
{
    public const string SeeAsPlayer = "see-as-player";
    public const string MeetTheCast = "meet-the-cast";
    public const string WalkTheJourney = "walk-the-journey";
    public const string StandSomewhere = "stand-somewhere";
    public const string OpenTheCampaign = "open-the-campaign";
    public const string JumpAnywhere = "jump-anywhere";
    public const string AskTheLoremaster = "ask-the-loremaster";
    public const string BackToGm = "back-to-gm";
    public const string AddSessionSix = "add-session-six";
    public const string WatchExtraction = "watch-extraction";
    public const string VetExtraction = "vet-extraction";
    public const string RevealSecret = "reveal-secret";
    public const string SeeWhatTheySee = "see-what-they-see";

    public static readonly IReadOnlyList<TutorialStep> Steps =
    [
        // Chapter 1 — playing in a world, in player view.
        new(SeeAsPlayer, "See it as a player", null,
            "Use the GM chip under the world name. The world tints, and what only a GM sees leaves the page.",
            TutorialTrigger.EnterPlayerView, InPlayerView: true),
        new(MeetTheCast, "Meet the cast", "/codex",
            "Browse what Nornis built from five sessions of notes: the people, the places, the factions, the things they carry.",
            TutorialTrigger.Visit, InPlayerView: true),
        new(WalkTheJourney, "Walk the journey", "/timeline",
            "Follow the party session by session. {/storylines} is the same record read by thread.",
            TutorialTrigger.Visit, InPlayerView: true),
        new(StandSomewhere, "Stand somewhere", "/locations",
            "Pick a place on the map and read what happened there.",
            TutorialTrigger.Visit, InPlayerView: true),
        new(OpenTheCampaign, "Open the campaign", "/campaigns",
            "The Vesper Bell is the run of play: its cast, its places and its sessions, assembled from what the notes cite.",
            TutorialTrigger.Visit, InPlayerView: true),
        new(JumpAnywhere, "Jump to anything", null,
            "Type a few letters of a name — Voss, Saltmere, the bell — and go straight there.",
            TutorialTrigger.Jump, InPlayerView: true),
        new(AskTheLoremaster, "Ask the Loremaster", "/dashboard",
            "Ask anything the party would know, from {/dashboard} or the Loremaster beside any entry.",
            TutorialTrigger.State, InPlayerView: true),

        // Chapter 2 — running a campaign.
        new(BackToGm, "Return to GM view", null,
            "Leave player view from the chip, and watch the navigation: a group only you see returns.",
            TutorialTrigger.LeavePlayerView),
        new(AddSessionSix, "Add the Session 6 notes", "/capture",
            "Paste the Session 6 notes as a new session and save them for processing.",
            TutorialTrigger.State),
        new(WatchExtraction, "Watch Nornis think", "/sources",
            "The new session works its way through extraction; {/sources} wears a count while it does.",
            TutorialTrigger.State),
        new(VetExtraction, "Vet the extraction", "/review",
            "Accept or correct at least one proposal. Nothing enters the record without you.",
            TutorialTrigger.State),
        new(RevealSecret, "Reveal the Castellan's design", "/convergence",
            "{/convergence} ranks what the party is ready to learn. Pick the Castellan and tick what they now know — or reveal the GM prep note whole from {/sources}.",
            TutorialTrigger.State),
        new(SeeWhatTheySee, "See what they see", "/learned",
            "View as player again and open {/learned}: the disclosure is there, with your note beside it.",
            TutorialTrigger.LearnedAsPlayer, InPlayerView: true),
    ];

    public static TutorialStep? Find(string key) => Steps.FirstOrDefault(s => s.Key == key);

    /// <summary>
    /// The step with the given trigger whose page the path belongs to — the page itself or
    /// anything under it, the way <see cref="NavGroups.FindByPath"/> reads a route.
    /// </summary>
    public static TutorialStep? StepAt(string path, TutorialTrigger trigger)
    {
        var normalized = "/" + path.Trim('/');
        return Steps
            .Where(s => s.Trigger == trigger && s.Href is not null)
            .FirstOrDefault(s => normalized.Equals(s.Href, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(s.Href + "/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Where the step's page sits in the sidebar, or null for a step that has no page.</summary>
    public static NavPlacement? Door(TutorialStep step) =>
        step.Href is null ? null : NavGroups.Locate(step.Href);

    /// <summary>
    /// The hint with every <c>{/route}</c> replaced by that page's sidebar label. A route the
    /// sidebar does not know is left in its braces, where the guide's tests will see it.
    /// </summary>
    public static string HintFor(TutorialStep step)
    {
        var hint = step.Hint;
        var open = hint.IndexOf("{/", StringComparison.Ordinal);
        while (open >= 0)
        {
            var close = hint.IndexOf('}', open);
            if (close < 0)
            {
                break;
            }

            var route = hint[(open + 1)..close];
            var label = NavGroups.FindByPath(route)?.Label;
            if (label is null)
            {
                open = hint.IndexOf("{/", close, StringComparison.Ordinal);
                continue;
            }

            hint = hint[..open] + label + hint[(close + 1)..];
            open = hint.IndexOf("{/", open + label.Length, StringComparison.Ordinal);
        }

        return hint;
    }
}
