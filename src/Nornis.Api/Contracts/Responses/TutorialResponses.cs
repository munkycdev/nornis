namespace Nornis.Api.Contracts.Responses;

public record TutorialStepResponse(string Key, int Chapter, bool ClientReported, DateTimeOffset? CompletedAt);

public record TutorialChecklistResponse(IReadOnlyList<TutorialStepResponse> Steps);

public record TutorialSessionSixResponse(string Body);
