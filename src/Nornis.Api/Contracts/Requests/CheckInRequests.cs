namespace Nornis.Api.Contracts.Requests;

/// <summary>One answer to a check-in question (feature 44). <c>Answer</c> is a <c>CheckInAnswer</c> name.</summary>
public record AnswerCheckInQuestionRequest(
    string Answer,
    /// <summary>A standing instruction to keep with the answer; empty keeps none.</summary>
    string? RuleText = null,
    /// <summary>Background only: the entry to fold the storyline into; null sets it Dormant.</summary>
    Guid? RecordOnArtifactId = null);

public record AddEditorialRuleRequest(string Text);
