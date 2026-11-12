namespace Nornis.Application.Prep;

/// <summary>
/// What a section's game blocks would lose on the way out (feature 35): an unnamed block files
/// on nothing, an empty one prints nothing, and a long one is cut. The warnings never change
/// the text; the GM decides. They read the cut through <see cref="GameBlocks.ExportLineFor"/>,
/// whose cut depends on the block alone, so a warning and the export can't disagree.
/// </summary>
public static class GameBlockWarnings
{
    private static readonly string[] Ordinals =
        ["first", "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth"];

    public static IReadOnlyList<string> For(string? markdown)
    {
        var warnings = new List<string>();
        var seen = new Dictionary<GameBlockKind, int>();

        foreach (var block in GameBlocks.InMarkdown(markdown))
        {
            var position = seen[block.Kind] = seen.GetValueOrDefault(block.Kind) + 1;
            var kind = block.Kind == GameBlockKind.Stat ? "stat block" : "item block";

            if (block.IsEmpty)
            {
                warnings.Add($"{Positional(position, kind)} is empty.");
            }
            else if (block.Name is not { } name)
            {
                var target = block.Kind == GameBlockKind.Stat ? "anyone" : "anything";
                warnings.Add($"{Positional(position, kind)} has no name, so Add to the record can't file it on {target}. "
                    + "Start it with \"# Name\".");
            }
            else if (GameBlocks.ExportLineFor(block, null)!.WasCut)
            {
                warnings.Add($"{name}'s {kind} is too long to file as one fact; Add to the record will cut it short.");
            }
        }

        return warnings;
    }

    private static string Positional(int position, string kind) =>
        position <= Ordinals.Length ? $"The {Ordinals[position - 1]} {kind}" : $"{char.ToUpperInvariant(kind[0])}{kind[1..]} {position}";
}
