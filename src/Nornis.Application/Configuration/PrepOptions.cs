namespace Nornis.Application.Configuration;

/// <summary>
/// Prep's bounds, enforced on every write: create, import, save, and the accept of a draft.
/// <c>PrepOptionsTests</c> pins the defaults so a whole Adventure always fits in one source.
/// </summary>
public class PrepOptions
{
    public const string SectionName = "Prep";

    public int MaxSections { get; set; } = 200;

    public int MaxSectionChars { get; set; } = 20_000;

    /// <summary>
    /// The Adventure's size as <see cref="Prep.AdventureSize"/> measures it: titles and bodies.
    /// Below <c>SourceService.MaxBodyChars</c> by the headroom an export's headings and filing
    /// line take, so any Adventure within it can be exported whole.
    /// </summary>
    public int MaxAdventureChars { get; set; } = 97_500;

    public int MaxInstructionChars { get; set; } = 2_000;

    public int MaxRevisionsPerSection { get; set; } = 20;

    /// <summary>The application timeout on one drafting call, like every other AI path's.</summary>
    public int DraftTimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// The deployment drafting runs on. Null runs it on the Loremaster's (<c>Loremaster:AiModel</c>).
    /// The name is also the key the ledger prices the call by, so one set here needs its own
    /// <c>ModelPricing</c> entry, or drafting is recorded at $0 and the Prep pool
    /// never fills.
    /// </summary>
    public string? Deployment { get; set; }
}
