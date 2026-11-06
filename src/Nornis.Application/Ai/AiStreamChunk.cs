using Nornis.Domain.Models;

namespace Nornis.Application.Ai;

/// <summary>
/// One piece of a streamed completion. Text arrives in order as the model writes it; the final
/// chunk carries empty text and the call's <see cref="Usage"/> — null there when the provider
/// reported none, which is not the same as a call that cost nothing. A stream that ends without
/// that final chunk was cut short, by a failure or by the caller.
/// </summary>
public sealed record AiStreamChunk
{
    public required string Text { get; init; }

    public AiUsage? Usage { get; init; }

    /// <summary>
    /// On the final chunk: the provider stopped because the call's output ceiling was reached,
    /// so the text ends wherever the model was when it ran out. Only a client that sets a
    /// ceiling can see this; for the rest it stays false.
    /// </summary>
    public bool CutOff { get; init; }

    /// <summary>
    /// On the final chunk of a call that could search the web (feature 40): the pages it drew on
    /// and how many searches it ran. Null on every other call.
    /// </summary>
    public WebResearchResult? WebResearch { get; init; }
}
