namespace Nornis.Api.Contracts.Requests;

public record CreateAdventureRequest(string Title, string? Pitch, Guid? CampaignId);

public record ImportAdventureFile(string Name, string Markdown);

public record ImportAdventureRequest(IReadOnlyList<ImportAdventureFile> Files, Guid? CampaignId);

/// <summary>
/// The Adventure's own fields, each the new value: a null pitch means no pitch, and a null
/// campaign means world-level prep. Sent whole, so there is no "unchanged" to confuse with "none".
/// </summary>
public record UpdateAdventureRequest(string Title, string? Pitch, Guid? CampaignId);

/// <summary>A GM's save of one section, naming the version of it the edit was based on.</summary>
public record SaveAdventureSectionRequest(string Kind, string Title, string? BodyMarkdown, DateTimeOffset BasedOnUpdatedAt);

public record AddAdventureSectionRequest(string Kind, string Title, Guid? AfterSectionId);

public record ReorderAdventureSectionsRequest(IReadOnlyList<Guid> SectionIds);

/// <summary>Draft or revise one section. A null or blank instruction is a plain Draft or Revise.</summary>
public record DraftAdventureSectionRequest(string? Instruction, DateTimeOffset BasedOnUpdatedAt);
