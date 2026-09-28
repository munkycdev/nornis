namespace Nornis.Api.Contracts.Responses;

/// <summary>
/// The server-sent event names an Ask answer streams under. The Web's <c>AskStreamEvents</c>
/// mirrors these — the two hosts share no project, so nothing but that comment holds them
/// together across the deploy boundary.
/// </summary>
public static class AskStreamEventNames
{
    /// <summary><see cref="AskStreamStartResponse"/>: grounded, priced, the model is starting.</summary>
    public const string Start = "start";

    /// <summary><see cref="AskStreamDeltaResponse"/>: the next piece of the answer text.</summary>
    public const string Delta = "delta";

    /// <summary><see cref="AskAnswerResponse"/>: the whole answer. The stream ends here.</summary>
    public const string Done = "done";

    /// <summary><see cref="ErrorResponse"/>: the model call failed partway. The stream ends here.</summary>
    public const string Error = "error";
}

public record AskStreamStartResponse(string Confidence, IReadOnlyList<string> Caveats);

public record AskStreamDeltaResponse(string Text);
