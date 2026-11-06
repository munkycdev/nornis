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

    /// <summary>
    /// Let the model search the web through its provider (feature 40). Null on every call but a
    /// research turn of Prep's side conversation, which is the only caller that sets it.
    /// </summary>
    public AiWebSearchRequest? WebSearch { get; init; }

    /// <summary>
    /// This call's own output ceiling in tokens, for a client that takes one. Null means the
    /// client's usual ceiling; never zero. Only a Design turn of Prep's side conversation sets it
    /// (feature 41), since it may write the whole draft.
    /// </summary>
    public int? OutputCeilingTokens { get; init; }
}

/// <summary>What a research turn asks of its call (feature 40).</summary>
/// <param name="MaxSearches">Searches the provider may run, where it can be held to a number.</param>
/// <param name="OutputCeilingTokens">The turn's own output ceiling: searching and the reasoning
/// around it count against it on some models.</param>
public sealed record AiWebSearchRequest(int MaxSearches, int OutputCeilingTokens);
