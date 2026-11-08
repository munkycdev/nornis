namespace Nornis.Api.Contracts.Responses;

/// <summary>The Key art card's state (feature 42). Mirrors <c>KeyArtState</c>.</summary>
public record KeyArtStateResponse(
    KeyArtResponse? KeyArt, string? Brief, IReadOnlyList<Guid> Options, Guid? ChosenOptionId,
    int ImagesThisMonth, int ImagesCap, bool CanGenerate, string? GenerateBlocker, string? Warning);

/// <summary><see cref="FocusY"/> is the anchor as a vertical position, 0 to 100, decided where the hero's is.</summary>
public record KeyArtResponse(string Source, string Anchor, string ContentType, DateTimeOffset UpdatedAt, int FocusY);

/// <summary>
/// The top of Home (feature 42). <see cref="HeroPictureResponse.Kind"/> is KeyArt, Map or None;
/// key art carries its stamp for the Web's image route, a map the URL the Map page uses.
/// </summary>
public record WorldHeroResponse(
    Guid WorldId, string Name, string? GameSystem, int SessionCount, DateTimeOffset? LastSessionAt, int EntryCount,
    HeroPictureResponse Picture);

public record HeroPictureResponse(string Kind, DateTimeOffset? KeyArtStamp, string? MapUrl, int FocusY);
