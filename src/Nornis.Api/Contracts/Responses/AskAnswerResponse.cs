namespace Nornis.Api.Contracts.Responses;

public record AskAnswerResponse(
    string Answer,
    IReadOnlyList<CitationResponse> Citations,
    string Confidence,
    IReadOnlyList<string> Caveats);
