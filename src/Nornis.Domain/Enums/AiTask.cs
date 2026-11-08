namespace Nornis.Domain.Enums;

/// <summary>
/// A kind of AI work that gets its own model choice (feature 39). Every
/// <see cref="AiOperationType"/> belongs to exactly one, through <c>AiTasks.For</c>.
/// </summary>
public enum AiTask
{
    /// <summary>The Loremaster and the other answers a GM reads.</summary>
    Ask,

    /// <summary>Everything that proposes canon, and reading images for it.</summary>
    Extraction,

    /// <summary>Prep drafting and the Adventure's side conversation.</summary>
    Prep,

    /// <summary>Short background text: world names, convergence notes.</summary>
    Chores,

    /// <summary>Generating pictures: an Adventure's cover (feature 38) and a world's key art
    /// (feature 42). An image model, not a chat model. Only a key holder's own key runs it;
    /// Nornis has no image deployment.</summary>
    CoverArt
}
