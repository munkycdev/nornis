using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

/// <summary>
/// The one table of which answers a question kind takes (feature 44). The service validates an
/// answer against it and the response carries it to the Web, so the buttons on screen and the
/// check on the server cannot disagree. Order is display order.
/// </summary>
public static class CheckInAnswers
{
    private static readonly CheckInAnswer[] StorylineAnswers =
        [CheckInAnswer.Pursuing, CheckInAnswer.Background, CheckInAnswer.Done, CheckInAnswer.Skip];

    private static readonly CheckInAnswer[] EntryAnswers =
        [CheckInAnswer.Keep, CheckInAnswer.Archive, CheckInAnswer.Skip];

    private static readonly CheckInAnswer[] PatternAnswers =
        [CheckInAnswer.Keep, CheckInAnswer.Stop, CheckInAnswer.Skip];

    public static IReadOnlyList<CheckInAnswer> AllowedFor(CheckInQuestionKind kind) => kind switch
    {
        CheckInQuestionKind.Storyline => StorylineAnswers,
        CheckInQuestionKind.Entry => EntryAnswers,
        CheckInQuestionKind.Pattern => PatternAnswers,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Every question kind names its answers here."),
    };

    public static bool IsAllowed(CheckInQuestionKind kind, CheckInAnswer answer) => AllowedFor(kind).Contains(answer);

    /// <summary>The status an answer sets on its subject, or null when it sets none. The merge
    /// (Background with an entry) is decided by the service, not here.</summary>
    public static ArtifactStatus? StatusFor(CheckInQuestionKind kind, CheckInAnswer answer) => (kind, answer) switch
    {
        (CheckInQuestionKind.Storyline, CheckInAnswer.Background) => ArtifactStatus.Dormant,
        (CheckInQuestionKind.Storyline, CheckInAnswer.Done) => ArtifactStatus.Resolved,
        (CheckInQuestionKind.Entry, CheckInAnswer.Archive) => ArtifactStatus.Archived,
        _ => null,
    };
}
