namespace Nornis.Application.Art;

/// <summary>
/// What a refine (feature 45) asks of the image model: keep the picture and redraw its detail.
/// The image API takes no seed, so this instruction is the only thing that holds the composition.
/// The caller's image rules come last, as in a generation, so a refine can't drop the no-text rule.
/// </summary>
public static class ArtRefinePrompt
{
    public const string Instruction =
        "Refine this picture. Keep its composition, framing, subjects, poses, palette and lighting exactly as they are. "
        + "Redraw it with sharper, more careful detail in faces, hands, textures and edges, and fix anything malformed. "
        + "Do not add, remove or move anything.";

    /// <param name="brief">The GM's current brief, for what the picture shows; null or blank leaves it out.</param>
    /// <param name="imageRules">The rules every request of this kind carries (cover or key art).</param>
    public static string For(string? brief, string imageRules) =>
        string.IsNullOrWhiteSpace(brief)
            ? Instruction + "\n\n" + imageRules
            : Instruction + "\n\nThe picture shows: " + brief.Trim() + "\n\n" + imageRules;
}
