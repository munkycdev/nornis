namespace Nornis.Application.Configuration;

/// <summary>
/// Prep's bounds, enforced on every write: create, import, save, the accept of a draft, and
/// each turn of the side conversation.
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

    public int MaxRevisionsPerSection { get; set; } = 20;

    /// <summary>The application timeout on one drafting call, like every other AI path's.</summary>
    public int DraftTimeoutSeconds { get; set; } = 90;

    /// <summary>The longest message a GM can send in an Adventure's side conversation (feature 30).</summary>
    public int MaxConversationMessageChars { get; set; } = 4_000;

    /// <summary>
    /// The most messages one side conversation holds, the GM's and the answers together. A turn
    /// needs room for two, so at one short of this the panel asks the GM to clear it.
    /// </summary>
    public int MaxConversationMessages { get; set; } = 400;

    /// <summary>
    /// The conversation so far that rides with a turn: the newest whole messages, until the next
    /// would cross this. Never a cut message.
    /// </summary>
    public int ConversationHistoryChars { get; set; } = 12_000;

    /// <summary>The section on screen, cut to this when it rides with a turn.</summary>
    public int ConversationSectionChars { get; set; } = 6_000;

    /// <summary>
    /// The deployment drafting runs on. Null runs it on the Loremaster's (<c>Loremaster:AiModel</c>).
    /// The name is also the key the ledger prices the call by, so one set here needs its own
    /// <c>ModelPricing</c> entry, or drafting is recorded at $0 and the Prep pool
    /// never fills.
    /// </summary>
    public string? Deployment { get; set; }

    /// <summary>How many cover images an Adventure may have generated, chosen or not (feature 38).</summary>
    public int MaxCoverImagesPerAdventure { get; set; } = 12;

    /// <summary>How many options one Generate makes (feature 38).</summary>
    public int CoverOptionsPerGeneration { get; set; } = 2;

    /// <summary>How long a cover generation may take, in seconds: an image takes far longer than text.</summary>
    public int CoverTimeoutSeconds { get; set; } = 180;

    /// <summary>The largest cover a GM may upload, in bytes (feature 38).</summary>
    public int MaxCoverUploadBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>The most web searches one research turn may run (feature 40), where the provider can cap them; the prompt asks for it everywhere.</summary>
    public int MaxWebSearchesPerTurn { get; set; } = 5;

    /// <summary>A research turn's output ceiling in tokens (feature 40): searching and the reasoning around it count against it on some models.</summary>
    public int WebResearchOutputCeilingTokens { get; set; } = 6_000;

    /// <summary>How long a research turn may take, in seconds (feature 40): searches take longer than an answer from the record.</summary>
    public int WebResearchTimeoutSeconds { get; set; } = 180;

    /// <summary>
    /// A Design turn's output ceiling in tokens (feature 41): enough for a whole draft at
    /// <c>Adventure.MaxDraftChars</c>, since the Loremaster may write or rewrite it in one answer.
    /// Kept under 16,384, the output limit some chat models have.
    /// </summary>
    public int DesignOutputCeilingTokens { get; set; } = 16_000;

    /// <summary>
    /// How long a Design turn may take, in seconds (feature 41). Under Azure Container Apps'
    /// 240-second request limit on the Web to API hop, with room for the write after it.
    /// </summary>
    public int DesignTimeoutSeconds { get; set; } = 220;

    /// <summary>The longest any one Prep AI call may run, in seconds: what a turn's or a draft's lease must outlive.</summary>
    public int LongestCallSeconds => new[] { DraftTimeoutSeconds, WebResearchTimeoutSeconds, DesignTimeoutSeconds }.Max();

    /// <summary>
    /// The first bound that is zero or less, by name, or null when every bound is positive. No
    /// bound has an "unbounded" value, so the API refuses to start on one (Program.cs).
    /// </summary>
    public string? FirstNonPositiveBound() =>
        new (string Name, int Value)[]
        {
            (nameof(MaxSections), MaxSections),
            (nameof(MaxCoverImagesPerAdventure), MaxCoverImagesPerAdventure),
            (nameof(CoverOptionsPerGeneration), CoverOptionsPerGeneration),
            (nameof(CoverTimeoutSeconds), CoverTimeoutSeconds),
            (nameof(MaxCoverUploadBytes), MaxCoverUploadBytes),
            (nameof(MaxSectionChars), MaxSectionChars),
            (nameof(MaxAdventureChars), MaxAdventureChars),
            (nameof(MaxRevisionsPerSection), MaxRevisionsPerSection),
            (nameof(DraftTimeoutSeconds), DraftTimeoutSeconds),
            (nameof(MaxConversationMessageChars), MaxConversationMessageChars),
            (nameof(MaxConversationMessages), MaxConversationMessages),
            (nameof(ConversationHistoryChars), ConversationHistoryChars),
            (nameof(ConversationSectionChars), ConversationSectionChars),
            (nameof(MaxWebSearchesPerTurn), MaxWebSearchesPerTurn),
            (nameof(WebResearchOutputCeilingTokens), WebResearchOutputCeilingTokens),
            (nameof(WebResearchTimeoutSeconds), WebResearchTimeoutSeconds),
            (nameof(DesignOutputCeilingTokens), DesignOutputCeilingTokens),
            (nameof(DesignTimeoutSeconds), DesignTimeoutSeconds),
        }.FirstOrDefault(b => b.Value <= 0).Name;
}
