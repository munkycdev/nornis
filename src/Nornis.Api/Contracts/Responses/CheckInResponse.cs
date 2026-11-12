namespace Nornis.Api.Contracts.Responses;

/// <summary>The check-in as the GM sees it (feature 44). Enum-valued fields are their names.</summary>
public record CheckInResponse(
    bool Due,
    int SessionsSinceLast,
    int CadenceSessions,
    int QuestionCount,
    int MaxRules,
    DateTimeOffset? LastCheckInAt,
    CheckInRoundResponse? Current,
    IReadOnlyList<EditorialRuleResponse> Rules);

public record CheckInRoundResponse(
    Guid Id,
    DateTimeOffset CreatedAt,
    IReadOnlyList<CheckInQuestionResponse> Questions);

public record CheckInQuestionResponse(
    Guid Id,
    int Order,
    bool Asked,
    string Kind,
    Guid? SubjectArtifactId,
    string SubjectLabel,
    string Text,
    string? Why,
    CheckInEvidenceResponse Evidence,
    string? SuggestedRule,
    Guid? SuggestedRecordOnArtifactId,
    IReadOnlyList<string> AllowedAnswers,
    string? Answer,
    DateTimeOffset? AnsweredAt,
    string? Outcome,
    Guid? RuleId);

public record CheckInEvidenceResponse(
    string? Summary,
    string? Status,
    int FactCount,
    int SourceCount,
    int SessionsQuiet,
    int OpenQuestionCount,
    string? FirstSeenTitle,
    DateTimeOffset? FirstSeenAt,
    IReadOnlyList<string> Facts,
    IReadOnlyList<CheckInRelatedEntryResponse> RelatedEntries,
    string? Predicate,
    int ArtifactCount,
    IReadOnlyList<string> Examples);

public record CheckInRelatedEntryResponse(Guid Id, string Name, string Type);

public record EditorialRuleResponse(
    Guid Id,
    string Text,
    string Origin,
    Guid? CheckInQuestionId,
    string? QuestionText,
    DateTimeOffset CreatedAt);

public record CheckInAnswerResponse(CheckInQuestionResponse Question, EditorialRuleResponse? Rule);
