namespace Nornis.Api.Contracts.Requests;

/// <summary>New position for a map pin, normalized 0..1 from the image's top-left.</summary>
public record MovePlacemarkRequest(decimal X, decimal Y);
