namespace Nornis.Web.State;

// Client-side conversation model, persisted to browser localStorage per world.
// Plain settable properties so it round-trips cleanly through System.Text.Json.

public class AskCitation
{
    public string DisplayName { get; set; } = string.Empty;
    public Guid? ArtifactId { get; set; }

    /// <summary>Library document, for passage citations ("Title, p. N").</summary>
    public Guid? DocumentId { get; set; }
}

public class AskExchange
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string Confidence { get; set; } = string.Empty;
    public List<AskCitation> Citations { get; set; } = [];
    public List<string> Caveats { get; set; } = [];
}

public class AskConversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "New conversation";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<AskExchange> Exchanges { get; set; } = [];
}
