using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>
/// The check-in as the GM sees it (feature 44): whether one is due, the open one with its
/// questions, and the world's standing instructions. One read, for Home and the page alike.
/// </summary>
public record CheckInView(
    bool Due,
    int SessionsSinceLast,
    int CadenceSessions,
    int QuestionCount,
    int MaxRules,
    DateTimeOffset? LastCheckInAt,
    CheckInRound? Current,
    IReadOnlyList<EditorialRuleView> Rules);

/// <summary>The open check-in: asked questions first, then the longer list, answered ones included.</summary>
public record CheckInRound(
    Guid Id,
    DateTimeOffset CreatedAt,
    IReadOnlyList<CheckInQuestionView> Questions);

public record CheckInQuestionView(
    Guid Id,
    int Order,
    bool Asked,
    CheckInQuestionKind Kind,
    Guid? SubjectArtifactId,
    string SubjectLabel,
    string Text,
    string? Why,
    CheckInEvidenceBlock Evidence,
    string? SuggestedRule,
    Guid? SuggestedRecordOnArtifactId,
    IReadOnlyList<CheckInAnswer> AllowedAnswers,
    CheckInAnswer? Answer,
    DateTimeOffset? AnsweredAt,
    /// <summary>What the answer did, in words ("Merged into Koves", "Set Dormant"); null while unanswered.</summary>
    string? Outcome,
    Guid? RuleId);

public record EditorialRuleView(
    Guid Id,
    string Text,
    EditorialRuleOrigin Origin,
    Guid? CheckInQuestionId,
    string? QuestionText,
    DateTimeOffset CreatedAt);

/// <summary>
/// A candidate's evidence (feature 44): what the chooser reads, what the page renders and what a
/// Background answer can record on. Stored on the question as JSON, written once. Every field is
/// bounded by the candidate caps.
/// </summary>
public sealed record CheckInEvidenceBlock
{
    public string? Summary { get; init; }

    public string? Status { get; init; }

    public int FactCount { get; init; }

    /// <summary>Distinct sources behind the subject, its facts and its relationships.</summary>
    public int SourceCount { get; init; }

    /// <summary>Dated sessions since the storyline's last development; zero when not quiet.</summary>
    public int SessionsQuiet { get; init; }

    public int OpenQuestionCount { get; init; }

    public string? FirstSeenTitle { get; init; }

    public DateTimeOffset? FirstSeenAt { get; init; }

    /// <summary>A few of the subject's facts, "predicate: value".</summary>
    public IReadOnlyList<string> Facts { get; init; } = [];

    /// <summary>Characters and factions the storyline is linked to or names: where Background can record it.</summary>
    public IReadOnlyList<CheckInRelatedEntry> RelatedEntries { get; init; } = [];

    /// <summary>A pattern's predicate, as most often written.</summary>
    public string? Predicate { get; init; }

    /// <summary>Entries carrying a pattern's predicate.</summary>
    public int ArtifactCount { get; init; }

    /// <summary>A few of a pattern's values.</summary>
    public IReadOnlyList<string> Examples { get; init; } = [];
}

public sealed record CheckInRelatedEntry(Guid Id, string Name, string Type);

/// <summary>
/// One thing the evidence says Nornis has reason to doubt (feature 44), before the chooser sees
/// it. <see cref="Key"/> is what the model answers with; <see cref="Text"/> and <see cref="Why"/>
/// are the templated question the unpicked ones keep.
/// </summary>
public sealed record CheckInCandidate(
    string Key,
    CheckInQuestionKind Kind,
    Guid? SubjectArtifactId,
    string SubjectLabel,
    string Text,
    string Why,
    CheckInEvidenceBlock Evidence);

/// <summary>
/// Everything candidate gathering reads (feature 44), hand-buildable so the rules are tested
/// without repositories. Read at GM visibility: the check-in is GM prep and GM-only material
/// counts.
/// </summary>
public sealed record CheckInEvidence(
    /// <summary>Every non-archived artifact of the world, storylines included.</summary>
    IReadOnlyList<Artifact> Artifacts,
    IReadOnlyList<ArtifactFact> Facts,
    IReadOnlyList<ArtifactRelationship> Relationships,
    /// <summary>Every source reference in the world: the provenance behind each artifact, fact and relationship.</summary>
    IReadOnlyList<SourceReference> References,
    /// <summary>Every source of the world, dated or not; titles and dates for the evidence lines.</summary>
    IReadOnlyList<Source> Sources,
    /// <summary>Dated sessions, newest first: the cadence axis.</summary>
    IReadOnlyList<Source> Sessions,
    /// <summary>Active storylines the continuity signal calls quiet, with their sessions since.</summary>
    IReadOnlyDictionary<Guid, int> QuietStorylines,
    /// <summary>Subjects answered in the remembered check-ins: not asked again now.</summary>
    IReadOnlySet<Guid> RecentlyAnsweredArtifactIds,
    /// <summary>Predicate keys answered Keep recently or Stop ever: not asked again.</summary>
    IReadOnlySet<string> SettledPredicates);

// ------------------------------------------------------------------- Commands --

public record AnswerCheckInQuestionCommand(
    Guid WorldId,
    Guid QuestionId,
    CheckInAnswer Answer,
    /// <summary>A standing instruction to write with the answer; empty or null writes none.</summary>
    string? RuleText,
    /// <summary>Background only: the entry to merge the storyline into; null sets it Dormant.</summary>
    Guid? RecordOnArtifactId,
    Guid ActingUserId,
    WorldRole ActingUserRole);

public record CheckInAnswerResult(CheckInQuestionView Question, EditorialRuleView? Rule);
