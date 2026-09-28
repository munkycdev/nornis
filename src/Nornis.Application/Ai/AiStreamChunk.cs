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
}
