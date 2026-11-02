using Nornis.Application.Errors;
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

/// <summary>
/// Draft or revise one section with AI. <see cref="Instruction"/> null is a plain Draft (or, on a
/// filled section, a plain tighten-and-improve Revise).
/// </summary>
public record DraftSectionCommand(
    Guid WorldId, Guid AdventureId, Guid SectionId, Guid ActingUserId, WorldRole ActingUserRole,
    DateTimeOffset BasedOnUpdatedAt, string? Instruction);

/// <summary>What a draft stream says, in order: text as it is written, then exactly one end.</summary>
public abstract record DraftEvent
{
    private DraftEvent()
    {
    }

    public sealed record Delta(string Text) : DraftEvent;

    /// <summary>The draft was saved. <see cref="RevisionCount"/> is how many Undos the section now has.</summary>
    public sealed record Completed(AdventureSection Section, int RevisionCount) : DraftEvent;

    /// <summary>Nothing was written to the section.</summary>
    public sealed record Failed(AppError Error) : DraftEvent;
}

/// <summary>The booklet's HTML, and the title File to Library will give it today.</summary>
public record AdventurePrint(Adventure Adventure, string Html, string LibraryTitle);

/// <summary>Export the whole Adventure, or the Chapter <see cref="ChapterSectionId"/> names and what follows it.</summary>
public record ExportAdventureCommand(
    Guid WorldId, Guid AdventureId, Guid? ChapterSectionId, Guid ActingUserId, WorldRole ActingUserRole);

/// <summary>
/// An Adventure's side conversation as the panel shows it, oldest first. Empty before the first
/// message and after a Clear. <see cref="IsFull"/> means there is no room for another turn.
/// </summary>
public record ConversationView(IReadOnlyList<ConversationMessage> Messages, bool IsFull);

/// <summary>
/// One turn of the side conversation. <see cref="SectionId"/> is the section on screen when the GM
/// sent it, or null when none was; it is context for the answer, never a target.
/// </summary>
public record SendConversationMessageCommand(
    Guid WorldId, Guid AdventureId, Guid ActingUserId, WorldRole ActingUserRole, string Text, Guid? SectionId);

/// <summary>What a turn's stream says, in order: the answer as it is written, then exactly one end.</summary>
public abstract record ConversationEvent
{
    private ConversationEvent()
    {
    }

    public sealed record Delta(string Text) : ConversationEvent;

    /// <summary>The answer ended cleanly and was saved.</summary>
    public sealed record Completed(ConversationMessage Answer) : ConversationEvent;

    /// <summary>No answer was saved. The GM's message was, before the call.</summary>
    public sealed record Failed(AppError Error) : ConversationEvent;
}

public enum PutInSectionMode
{
    /// <summary>The answer becomes the section's body.</summary>
    Replace,

    /// <summary>The answer goes after the section's body, a blank line between. An unwritten section is replaced.</summary>
    Append,
}

/// <summary>Copies one saved answer into a section, as an undoable revision. No AI call.</summary>
public record PutInSectionCommand(
    Guid WorldId, Guid AdventureId, Guid SectionId, WorldRole ActingUserRole,
    Guid MessageId, PutInSectionMode Mode, DateTimeOffset BasedOnUpdatedAt);

/// <summary>The section as Put in section left it, and how many Undos it now has.</summary>
public record PutInSectionResult(AdventureSection Section, int RevisionCount);
