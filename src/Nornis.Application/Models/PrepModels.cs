using Nornis.Application.Prep;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>An Adventure with its sections in order, and how many revisions each section can undo.</summary>
public record AdventureView(Adventure Adventure, IReadOnlyDictionary<Guid, int> RevisionCounts);

public record CreateAdventureCommand(
    Guid WorldId, Guid ActingUserId, WorldRole ActingUserRole, string Title, string? Pitch, Guid? CampaignId);

public record ImportAdventureCommand(
    Guid WorldId, Guid ActingUserId, WorldRole ActingUserRole, IReadOnlyList<MarkdownFile> Files, Guid? CampaignId);

/// <summary>
/// Replaces the Adventure's own fields. Every field is the new value, so null has one meaning
/// each: no pitch, and no campaign (world-level prep). There is no "leave unchanged".
/// </summary>
public record UpdateAdventureCommand(
    Guid WorldId, Guid AdventureId, WorldRole ActingUserRole, string Title, string? Pitch, Guid? CampaignId);

public record SaveSectionCommand(
    Guid WorldId, Guid AdventureId, Guid SectionId, WorldRole ActingUserRole,
    AdventureSectionKind Kind, string Title, string BodyMarkdown, DateTimeOffset BasedOnUpdatedAt);

/// <summary>Adds an empty section after <see cref="AfterSectionId"/>, or first when it is null.</summary>
public record AddSectionCommand(
    Guid WorldId, Guid AdventureId, WorldRole ActingUserRole,
    AdventureSectionKind Kind, string Title, Guid? AfterSectionId);

public record DeleteSectionCommand(Guid WorldId, Guid AdventureId, Guid SectionId, WorldRole ActingUserRole);

public record ReorderSectionsCommand(
    Guid WorldId, Guid AdventureId, WorldRole ActingUserRole, IReadOnlyList<Guid> OrderedSectionIds);

public record DeleteAdventureCommand(Guid WorldId, Guid AdventureId, WorldRole ActingUserRole);

public record UndoSectionCommand(Guid WorldId, Guid AdventureId, Guid SectionId, WorldRole ActingUserRole);
