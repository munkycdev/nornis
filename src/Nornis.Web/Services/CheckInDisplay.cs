using Nornis.Web.ApiClient;

namespace Nornis.Web.Services;

/// <summary>
/// The words the check-in (feature 44) shows for what the API sends as names: an answer, a
/// question's kind, and the one evidence line under a question. Kept here so the Home card and
/// the Check-in page say the same thing, and so the mapping is testable without a render.
/// </summary>
public static class CheckInDisplay
{
    public static string AnswerLabel(string answer) => answer switch
    {
        "Stop" => "Stop recording",
        _ => answer,
    };

    /// <summary>What the answer will do, as the button's title.</summary>
    public static string AnswerHint(string kind, string answer) => (kind, answer) switch
    {
        ("Storyline", "Pursuing") => "The table is on it. Nornis asks again if it stays quiet.",
        ("Storyline", "Background") => "Fold it into the entry chosen above, or set it Dormant.",
        ("Storyline", "Done") => "Set it Resolved.",
        ("Entry", "Keep") => "Leave the entry as it is.",
        ("Entry", "Archive") => "Archive the entry. Its page can restore it.",
        ("Pattern", "Keep") => "Keep recording these facts.",
        ("Pattern", "Stop") => "Stop recording these. A standing instruction says so from now on.",
        (_, "Skip") => "Ask again another time.",
        _ => string.Empty,
    };

    public static string KindLabel(string kind) => kind;

    /// <summary>The evidence under a question, in one line: what Nornis saw that made it ask.</summary>
    public static string EvidenceLine(CheckInQuestionDto question)
    {
        var e = question.Evidence;
        var parts = new List<string>();

        switch (question.Kind)
        {
            case "Storyline":
                if (!string.IsNullOrWhiteSpace(e.Status))
                    parts.Add(e.Status);
                if (e.SessionsQuiet > 0)
                    parts.Add($"{Count(e.SessionsQuiet, "session")} quiet");
                if (e.OpenQuestionCount > 0)
                    parts.Add($"{Count(e.OpenQuestionCount, "open question")}");
                parts.Add($"{Count(e.FactCount, "fact")} from {Count(e.SourceCount, "source")}");
                break;

            case "Entry":
                parts.Add($"{Count(e.FactCount, "fact")} from {Count(e.SourceCount, "source")}");
                if (!string.IsNullOrWhiteSpace(e.FirstSeenTitle))
                    parts.Add($"first seen in “{e.FirstSeenTitle}”" + (e.FirstSeenAt is { } at ? $" ({DateDisplay.Day(at)})" : ""));
                break;

            case "Pattern":
                parts.Add($"“{e.Predicate}” on {Count(e.ArtifactCount, "entry", "entries")}, {Count(e.FactCount, "fact")} in all");
                break;
        }

        return string.Join(" · ", parts);
    }

    public static string Count(int n, string one, string? many = null) =>
        $"{n} {(n == 1 ? one : many ?? one + "s")}";
}
