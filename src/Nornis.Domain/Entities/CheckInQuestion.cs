using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

/// <summary>
/// One question on a check-in (feature 44): a subject, the text the GM reads, the evidence it
/// rests on, and, once answered, what the answer did. Asked questions carry the model's phrasing;
/// the rest carry the templated text the candidate was born with.
/// </summary>
public class CheckInQuestion
{
    public const int MaxTextChars = 1000;
    public const int MaxLabelChars = 300;

    public Guid Id { get; set; }

    public Guid CheckInId { get; set; }

    /// <summary>Asked questions first in the model's order, then the rest in rank order.</summary>
    public int Order { get; set; }

    /// <summary>True for the questions the model chose; those are what Home shows.</summary>
    public bool Asked { get; set; }

    public CheckInQuestionKind Kind { get; set; }

    /// <summary>The storyline or entry the question is about; null for a pattern.</summary>
    public Guid? SubjectArtifactId { get; set; }

    /// <summary>The subject's name, or the pattern's predicate.</summary>
    public string SubjectLabel { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    /// <summary>One line of evidence: why Nornis asks.</summary>
    public string? Why { get; set; }

    /// <summary>
    /// The candidate's evidence as a JSON object (<c>CheckInEvidenceBlock</c>): the related entries
    /// a Background answer can record on, a pattern's predicate and counts. Written once.
    /// </summary>
    public string EvidenceJson { get; set; } = "{}";

    /// <summary>A standing instruction the model suggested, prefilled on the answer; null when none.</summary>
    public string? SuggestedRule { get; set; }

    /// <summary>For a storyline that reads as background: the entry the model suggests recording it on.</summary>
    public Guid? SuggestedRecordOnArtifactId { get; set; }

    public CheckInAnswer? Answer { get; set; }

    public DateTimeOffset? AnsweredAt { get; set; }

    public Guid? AnsweredByUserId { get; set; }

    /// <summary>The accepted batch the answer filed (a status change or a merge); null when the answer changed nothing.</summary>
    public Guid? ActionBatchId { get; set; }

    /// <summary>The entry a Background answer merged the storyline into.</summary>
    public Guid? RecordedOnArtifactId { get; set; }

    /// <summary>The standing instruction the answer wrote; null when it wrote none.</summary>
    public Guid? RuleId { get; set; }

    public bool IsAnswered => Answer is not null;

    // Navigation properties
    public CheckIn CheckIn { get; set; } = null!;
}
