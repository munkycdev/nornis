namespace Nornis.Api.Contracts.Requests;

/// <summary>
/// The world's campaigns in the order the GM wants them read. Any campaign the client omits
/// keeps a position — trailing the named ones — rather than losing its place.
/// </summary>
public record ReorderCampaignsRequest(IReadOnlyList<Guid> CampaignIds);
