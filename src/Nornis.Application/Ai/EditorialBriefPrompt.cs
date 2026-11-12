using System.Text;

namespace Nornis.Application.Ai;

/// <summary>
/// Renders a world's standing instructions for a prompt (feature 44). One function, because two
/// prompts read the brief: extraction, where the rules override the general guidance, and the
/// check-in's chooser, which must not ask what the GM has already settled. Empty renders empty,
/// so a world with no instructions sees the prompt it always saw.
/// </summary>
public static class EditorialBriefPrompt
{
    public const string Heading = "## Standing Instructions";

    public static string Render(IReadOnlyList<string> rules)
    {
        if (rules.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine(Heading);
        sb.AppendLine("The GM has given these standing instructions for this world. They take precedence over the");
        sb.AppendLine("general guidance that follows: a detail an instruction says is not tracked is Incidental,");
        sb.AppendLine("however clearly the source states it, and a thing an instruction says how to record is");
        sb.AppendLine("recorded that way.");
        for (var i = 0; i < rules.Count; i++)
        {
            sb.AppendLine($"{i + 1}. {rules[i].Trim()}");
        }

        return sb.ToString().TrimEnd();
    }
}
