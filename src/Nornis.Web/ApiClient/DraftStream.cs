namespace Nornis.Web.ApiClient;

/// <summary>
/// One step of a streamed Prep draft. Like <see cref="AskStreamEvent"/>, a stream always ends with
/// exactly one <see cref="Done"/> or <see cref="Failed"/>, including a refusal before streaming
/// and a connection that drops partway. Only <see cref="Done"/> means the section was saved.
/// </summary>
public abstract record DraftStreamEvent
{
    private DraftStreamEvent() { }

    public sealed record Delta(string Text) : DraftStreamEvent;

    /// <summary>The section as the draft left it, with its new Undo count.</summary>
    public sealed record Done(AdventureSectionDto Section) : DraftStreamEvent;

    public sealed record Failed(ApiError Error) : DraftStreamEvent;
}

/// <summary>The server-sent event names. Mirrors <c>DraftStreamEventNames</c> in the API, pinned by a test.</summary>
internal static class DraftStreamEventNames
{
    public const string Delta = "delta";
    public const string Done = "done";
    public const string Error = "error";
}
