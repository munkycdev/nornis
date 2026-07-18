namespace Nornis.Application.Ai;

/// <summary>
/// Vision extraction for Map sources: a map image in, labeled places with normalized
/// positions out (strict JSON). The service turns places into review proposals — the
/// client never touches the database.
/// </summary>
public interface IMapExtractionClient
{
    Task<MapExtractionResponse> ExtractAsync(MapExtractionRequest request, CancellationToken ct);
}

/// <summary>An existing Location artifact offered to the model for matching.</summary>
public sealed record MapLocationContext(Guid Id, string Name);

public class MapExtractionRequest
{
    public required byte[] ImageBytes { get; init; }

    public required string MediaType { get; init; }

    public string? SourceTitle { get; init; }

    /// <summary>The user's typed notes — naming context only, not separately extracted.</summary>
    public string? SourceBody { get; init; }

    public required IReadOnlyList<MapLocationContext> ExistingLocations { get; init; }

    public required string Model { get; init; }

    public required int TimeoutSeconds { get; init; }
}

/// <summary>One labeled place read off the map; X/Y normalized 0..1 from top-left.</summary>
public sealed record MapPlace(
    string Name,
    string? Kind,
    decimal X,
    decimal Y,
    decimal? Confidence,
    Guid? ExistingArtifactId);

public class MapExtractionResponse
{
    public required IReadOnlyList<MapPlace> Places { get; init; }

    public required int InputTokens { get; init; }

    public required int OutputTokens { get; init; }

    public required int TotalTokens { get; init; }

    public required int DurationMs { get; init; }

    public required string Model { get; init; }
}
