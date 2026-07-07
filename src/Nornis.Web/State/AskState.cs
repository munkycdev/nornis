namespace Nornis.Web.State;

/// <summary>
/// Carries a question from the dashboard's Ask hero to the dedicated /ask conversation page
/// within the same Blazor Server circuit, so a quick ask starts a full threaded conversation.
/// </summary>
public class AskState
{
    public string? PendingQuestion { get; set; }
}
