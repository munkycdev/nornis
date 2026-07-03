namespace Nornis.Api.Contracts.Requests;

public record AskLoremasterRequest(
    string Question,
    string? ConversationContext = null);
