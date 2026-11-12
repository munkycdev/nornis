using Nornis.Web.ApiClient;

namespace Nornis.Web.State;

/// <summary>The groups of world settings, one per card on the Settings page, each with its own Save.</summary>
public enum WorldSettingsSection
{
    /// <summary>Name, description and game system.</summary>
    World,

    /// <summary>The public link and the public Ask budget, which is what switches public questions on.</summary>
    PublicPage,

    /// <summary>The daily AI budget and Prep's monthly pool.</summary>
    Budgets,

    /// <summary>Whether refreshed summaries go through the review queue.</summary>
    Review,

    /// <summary>Whether the world is a template master.</summary>
    Template,
}

/// <summary>
/// What the GM has typed into the Settings page (Nornis 3.23). The page spreads these fields over
/// several tabs and keeps one form for all of them, so moving between tabs keeps what was typed.
/// Each Save sends only its own section: the API's update is partial (a null field is left as it
/// is), so saving the public link never sends a half-typed name from another tab.
/// </summary>
public sealed class WorldSettingsForm
{
    public Guid WorldId { get; private set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? GameSystem { get; set; }

    public string? PublicSlug { get; set; }
    public bool PublicAccessEnabled { get; set; }
    public decimal? PublicAskMonthlyBudget { get; set; }

    public decimal? DailyAiBudget { get; set; }
    public decimal? PrepMonthlyBudget { get; set; }

    public bool SummaryReviewRequired { get; set; }
    public bool IsTemplate { get; set; }

    /// <summary>The form filled from a world as the API last answered it.</summary>
    public static WorldSettingsForm From(WorldSummary world) => new()
    {
        WorldId = world.Id,
        Name = world.Name,
        Description = world.Description,
        GameSystem = world.GameSystem,
        PublicSlug = world.PublicSlug,
        PublicAccessEnabled = world.PublicAccessEnabled,
        PublicAskMonthlyBudget = world.PublicAskMonthlyBudgetUsd,
        DailyAiBudget = world.DailyAiBudgetUsd,
        PrepMonthlyBudget = world.PrepMonthlyBudgetUsd,
        SummaryReviewRequired = world.SummaryReviewRequired,
        IsTemplate = world.IsTemplate,
    };

    /// <summary>
    /// What a section must have before it can be saved, or null when it is ready. The API checks
    /// the same and more (budget ranges, slug shape, a slug already taken); this only stops a Save
    /// the API would refuse for a reason the page can say first.
    /// </summary>
    public string? Problem(WorldSettingsSection section) =>
        section == WorldSettingsSection.World && string.IsNullOrWhiteSpace(Name) ? "Name is required." : null;

    /// <summary>
    /// Whether a section holds something its Save has not sent: the form against the world as the
    /// API last answered it. Text is compared the way <see cref="RequestFor"/> sends it, so a
    /// trailing space or an empty box where the world has nothing is not a change.
    /// </summary>
    public bool IsChanged(WorldSettingsSection section, WorldSummary saved) => section switch
    {
        WorldSettingsSection.World =>
            Name.Trim() != saved.Name
            || (Description ?? string.Empty) != (saved.Description ?? string.Empty)
            || (GameSystem ?? string.Empty) != (saved.GameSystem ?? string.Empty),
        WorldSettingsSection.PublicPage =>
            // An emptied slug is not sent, so it is not a pending change either.
            (!string.IsNullOrWhiteSpace(PublicSlug) && PublicSlug.Trim().ToLowerInvariant() != (saved.PublicSlug ?? string.Empty))
            || PublicAccessEnabled != saved.PublicAccessEnabled
            || PublicAskMonthlyBudget != saved.PublicAskMonthlyBudgetUsd,
        WorldSettingsSection.Budgets =>
            DailyAiBudget != saved.DailyAiBudgetUsd || PrepMonthlyBudget != saved.PrepMonthlyBudgetUsd,
        WorldSettingsSection.Review => SummaryReviewRequired != saved.SummaryReviewRequired,
        WorldSettingsSection.Template => IsTemplate != saved.IsTemplate,
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
    };

    /// <summary>
    /// The update for one section, every other field left null so the API keeps it. An emptied
    /// description or game system is sent as empty text (null would mean "unchanged"); an emptied
    /// budget is sent as its Clear flag; an emptied slug is left as it was, as before.
    /// </summary>
    public UpdateWorldRequest RequestFor(WorldSettingsSection section) => section switch
    {
        WorldSettingsSection.World => new UpdateWorldRequest(
            Name: Name.Trim(),
            Description: Description ?? string.Empty,
            GameSystem: GameSystem ?? string.Empty),
        WorldSettingsSection.PublicPage => new UpdateWorldRequest(
            PublicSlug: string.IsNullOrWhiteSpace(PublicSlug) ? null : PublicSlug.Trim(),
            PublicAccessEnabled: PublicAccessEnabled,
            PublicAskMonthlyBudgetUsd: PublicAskMonthlyBudget,
            ClearPublicAskBudget: PublicAskMonthlyBudget is null),
        WorldSettingsSection.Budgets => new UpdateWorldRequest(
            DailyAiBudgetUsd: DailyAiBudget,
            ClearDailyAiBudget: DailyAiBudget is null,
            PrepMonthlyBudgetUsd: PrepMonthlyBudget,
            ClearPrepBudget: PrepMonthlyBudget is null),
        WorldSettingsSection.Review => new UpdateWorldRequest(SummaryReviewRequired: SummaryReviewRequired),
        WorldSettingsSection.Template => new UpdateWorldRequest(IsTemplate: IsTemplate),
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
    };
}
