using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

/// <summary>
/// One task's model on an OpenAI-compatible key (feature 39): the user's own catalog line, since
/// Nornis can't know what such an endpoint serves or what it costs. At most one per task.
/// </summary>
public class UserAiCompatibleModel
{
    public Guid Id { get; set; }

    public Guid UserAiKeyId { get; set; }

    public AiTask Task { get; set; }

    /// <summary>The endpoint's model or deployment name, as sent on the wire.</summary>
    public string ModelName { get; set; } = string.Empty;

    public bool SupportsImages { get; set; }

    /// <summary>USD per million input tokens. Zero means free, never "unknown".</summary>
    public decimal InputPerMillionUsd { get; set; }

    /// <summary>USD per million output tokens. Zero means free, never "unknown".</summary>
    public decimal OutputPerMillionUsd { get; set; }
}
