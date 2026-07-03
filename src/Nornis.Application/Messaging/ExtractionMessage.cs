namespace Nornis.Application.Messaging;

public record ExtractionMessage(Guid SourceId, Guid CampaignId);
