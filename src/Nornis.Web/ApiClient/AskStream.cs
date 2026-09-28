namespace Nornis.Web.ApiClient;

/// <summary>
/// One step of a streamed Loremaster answer, as the Web sees it. A stream always ends with
/// exactly one <see cref="Done"/> or <see cref="Failed"/> — including when the request never got
/// as far as streaming (a validation or budget refusal, an unreachable API) and when the
/// connection drops partway, so a reader never has to decide what silence means.
/// </summary>
public abstract record AskStreamEvent
{
    private AskStreamEvent() { }

    /// <summary>What retrieval already decided, before the model writes anything.</summary>
    public sealed record Started(string Confidence, IReadOnlyList<string> Caveats) : AskStreamEvent;

    /// <summary>The next piece of the answer text, [ref:…] markers included.</summary>
    public sealed record Delta(string Text) : AskStreamEvent;

    public sealed record Done(AskAnswer Answer) : AskStreamEvent;

    public sealed record Failed(ApiError Error) : AskStreamEvent;
}

/// <summary>
/// The server-sent event names. Mirrors <c>AskStreamEventNames</c> in the API — the two hosts
/// share no project, so nothing but this comment holds them together across the deploy boundary.
/// </summary>
internal static class AskStreamEventNames
{
    public const string Start = "start";
    public const string Delta = "delta";
    public const string Done = "done";
    public const string Error = "error";
}
