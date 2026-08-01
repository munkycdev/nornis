namespace Nornis.Application.Ai;

/// <summary>
/// The request shape shared by every prompt-in/JSON-out AI client. Five per-client
/// records used to carry these same four fields verbatim; the client interface, not the
/// request type, is what distinguishes the calls.
/// </summary>
public class AiPromptRequest
{
    public required string SystemPrompt { get; init; }

    public required string UserMessage { get; init; }

    public required string Model { get; init; }

    public required int TimeoutSeconds { get; init; }
}
