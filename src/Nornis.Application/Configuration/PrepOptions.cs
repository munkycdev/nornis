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

    public int MaxInstructionChars { get; set; } = 2_000;

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

    /// <summary>The section on screen, cut to this when it rides with a turn, as a neighbour is in a draft.</summary>
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
            (nameof(MaxInstructionChars), MaxInstructionChars),
            (nameof(MaxRevisionsPerSection), MaxRevisionsPerSection),
            (nameof(DraftTimeoutSeconds), DraftTimeoutSeconds),
            (nameof(MaxConversationMessageChars), MaxConversationMessageChars),
            (nameof(MaxConversationMessages), MaxConversationMessages),
            (nameof(ConversationHistoryChars), ConversationHistoryChars),
            (nameof(ConversationSectionChars), ConversationSectionChars),
        }.FirstOrDefault(b => b.Value <= 0).Name;
}
