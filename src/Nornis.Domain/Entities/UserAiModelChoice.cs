using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

/// <summary>
/// The model a user picked for one task (feature 39). No row means the task's default, never
/// "off".
/// </summary>
public class UserAiModelChoice
{
    public Guid UserId { get; set; }

    public AiTask Task { get; set; }

    /// <summary>A catalog id (<c>openai/gpt-5.4</c>), or <c>compatible:{UserAiCompatibleModel.Id}</c>.</summary>
    public string ModelId { get; set; } = string.Empty;

    public const string CompatiblePrefix = "compatible:";
}
