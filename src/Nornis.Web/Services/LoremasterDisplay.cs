using System.Text;
using System.Text.RegularExpressions;
using MudBlazor;
using Nornis.Web.State;

namespace Nornis.Web.Services;

/// <summary>
/// Shared presentation rules for Loremaster answers — used by the members' Ask page and
/// the public world's single-shot Ask so the two surfaces can't drift apart.
/// </summary>
public static partial class LoremasterDisplay
{
    /// <summary>Strips inline [ref:…] markers the model emits; citations render separately.</summary>
    public static string CleanAnswer(string text) => RefMarker().Replace(text, "").Trim();

    public static Color ConfidenceColor(string confidence) => confidence switch
    {
        "High" => Color.Success,
        "Medium" => Color.Info,
        "Low" => Color.Warning,
        _ => Color.Default,
    };

    /// <summary>
    /// Per-world localStorage key for Ask conversation history. The side panel and the
    /// /ask page share one history, so this literal must never fork.
    /// </summary>
    public static string StorageKey(Guid? worldId) => $"nornis:ask:{worldId}";

    /// <summary>
    /// The conversation-context preamble sent with follow-up questions. Both Ask
    /// surfaces must emit the identical shape — the model reads one convention.
    /// </summary>
    public static string? BuildContext(AskConversation c)
    {
        if (c.Exchanges.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Earlier in this conversation (most recent last):");
        foreach (var ex in c.Exchanges.TakeLast(5))
        {
            sb.AppendLine($"Q: {ex.Question}");
            sb.AppendLine($"A: {ex.Answer}");
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"\s*\[ref:[^\]]+\]")]
    private static partial Regex RefMarker();
}
