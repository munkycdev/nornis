namespace Nornis.Api.Contracts.Requests;

/// <summary>The GM's brief for key art (feature 42): saved as typed, or sent to Generate.</summary>
public record KeyArtBriefRequest(string? Brief);

/// <summary>Makes one generated option the key art.</summary>
public record ChooseKeyArtRequest(Guid OptionId);

/// <summary>Centre, Top or Bottom: the part of the picture the hero keeps.</summary>
public record SetKeyArtAnchorRequest(string? Anchor);
