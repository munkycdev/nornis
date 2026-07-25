namespace Nornis.Application.Ai;

/// <summary>
/// Where the party last was, carried forward from the nearest prior timeline source that
/// holds accepted Location links. A hint for the extraction model — "unless this source says
/// otherwise, the party is probably still here" — never canon: only accepted references feed
/// it, and the prompt forbids proposals grounded solely in it.
/// </summary>
public class RecentLocationContext
{
    /// <summary>Title of the prior source the locations were carried from.</summary>
    public required string SourceTitle { get; init; }

    /// <summary>When that prior source occurred, when dated.</summary>
    public DateTimeOffset? OccurredAt { get; init; }

    public required IReadOnlyList<PriorLocation> Locations { get; init; }
}

public class PriorLocation
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Summary { get; init; }
}
