namespace Nornis.Application.Ai;

/// <summary>
/// The request shape shared by every prompt-in/JSON-out AI client. Five per-client
/// records used to carry these same four fields verbatim; the client interface, not the
/// request type, is what distinguishes the calls.
/// </summary>
public class AiPromptRequest
{
    /// <summary>Whose call this is: decides whose key pays (feature 39).</summary>
    public required AiCallScope Scope { get; init; }

    public required string SystemPrompt { get; init; }

    public required string UserMessage { get; init; }

    /// <summary>The model Nornis's own key runs this work on (a deployment name). A user's own
    /// key may run another model, chosen per task (feature 39).</summary>
    public required string Model { get; init; }

    public required int TimeoutSeconds { get; init; }
}
