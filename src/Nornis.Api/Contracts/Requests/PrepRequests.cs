namespace Nornis.Api.Contracts.Requests;

public record CreateAdventureRequest(string Title, string? Pitch, Guid? CampaignId);

public record ImportAdventureFile(string Name, string Markdown);

public record ImportAdventureRequest(IReadOnlyList<ImportAdventureFile> Files, Guid? CampaignId);

/// <summary>
/// The Adventure's own fields, each the new value: a null pitch means no pitch, and a null
/// campaign means world-level prep. Sent whole, so there is no "unchanged" to confuse with "none".
/// </summary>
public record UpdateAdventureRequest(string Title, string? Pitch, Guid? CampaignId);

/// <summary>The four editable details, all of them: each is the new value, and null clears it (feature 32).</summary>
public record UpdateAdventureDetailsRequest(string? GameSystem, string? Edition, string? Levels, int? Year);

/// <summary>The GM's own AI-use tick (feature 38); ignored once AI use is recorded.</summary>
public record SetAiDeclaredRequest(bool Declared);

/// <summary>A GM's save of one section, naming the version of it the edit was based on.</summary>
public record SaveAdventureSectionRequest(string Kind, string Title, string? BodyMarkdown, DateTimeOffset BasedOnUpdatedAt);

public record AddAdventureSectionRequest(string Kind, string Title, Guid? AfterSectionId);

public record ReorderAdventureSectionsRequest(IReadOnlyList<Guid> SectionIds);

/// <summary>Draft or revise one section. A null or blank instruction is a plain Draft or Revise.</summary>
public record DraftAdventureSectionRequest(string? Instruction, DateTimeOffset BasedOnUpdatedAt);

/// <summary>Export to Nornis. A null chapter is the whole Adventure; otherwise one Chapter and the sections under it.</summary>
public record ExportAdventureRequest(Guid? ChapterSectionId);

/// <summary>One turn of the side conversation. <see cref="SectionId"/> is the section on screen, or null.</summary>
/// <param name="Scope">Section (the default) or Draft (feature 37): what the conversation sees besides the outline.</param>
/// <param name="WebResearch">The Web research switch (feature 40): this turn may search the web. Off when absent.</param>
public record SendConversationMessageRequest(string? Text, Guid? SectionId, string? Scope = null, bool WebResearch = false);

/// <summary>Put one saved answer into a section. <see cref="Mode"/> is Replace or Append.</summary>
public record PutInSectionRequest(Guid MessageId, string? Mode, DateTimeOffset BasedOnUpdatedAt);

/// <summary>The GM's art brief (feature 38): saved as they edit it, and what Generate paints from.</summary>
public record CoverBriefRequest(string? Brief);

/// <summary>Makes one generated option the cover.</summary>
public record ChooseCoverRequest(Guid OptionId);

/// <summary>FullBleed, Framed or ClassicModule.</summary>
public record SetCoverLayoutRequest(string? Layout);
