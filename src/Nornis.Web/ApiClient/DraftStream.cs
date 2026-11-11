namespace Nornis.Web.ApiClient;

/// <summary>The server-sent event names. Mirrors <c>DraftStreamEventNames</c> in the API, pinned by a test.</summary>
internal static class DraftStreamEventNames
{
    public const string Delta = "delta";
    public const string Done = "done";
    public const string Error = "error";

    /// <summary>A Design turn's answer began an edit (feature 41).</summary>
    public const string Writing = "writing";
}

/// <summary>
/// One step of a side-conversation turn. Like <see cref="AskStreamEvent"/>, a stream always ends
/// with exactly one <see cref="Done"/> or <see cref="Failed"/>, including a refusal before
/// streaming and a connection that drops partway; only <see cref="Done"/> means the answer was
/// saved. The GM's message was saved before the stream began, either way.
/// </summary>
public abstract record ConversationStreamEvent
{
    private ConversationStreamEvent() { }

    public sealed record Delta(string Text) : ConversationStreamEvent;

    /// <summary>A Design turn's answer began an edit, which goes into the draft, not the chat (feature 41).</summary>
    public sealed record WritingDraft : ConversationStreamEvent;

    public sealed record Done(ConversationMessageDto Answer) : ConversationStreamEvent;

    public sealed record Failed(ApiError Error) : ConversationStreamEvent;
}
