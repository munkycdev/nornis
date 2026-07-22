namespace Nornis.Api.Contracts.Requests;

/// <summary>GM-only fact removal; the note becomes the GM-note record of the correction.</summary>
public record RemoveFactRequest(string Note);
