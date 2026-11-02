using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Prep;

/// <summary>
/// The house skeleton a new Adventure starts from: Overview, Session clock, Chapter 1, three
/// Scenes and a Quick reference. A template, not a schema; the GM may delete, add, rename,
/// re-kind and reorder any of it. Nothing else builds sections from scratch.
/// </summary>
public static class AdventureSkeleton
{
    /// <summary>A Scene's four parts are subheadings in its body, so one editor stays one editor.</summary>
    public const string SceneTemplate = "### Goal\n\n### Read-aloud\n\n### Likely questions\n\n### End state";

    /// <summary>
    /// Bodies that count as unfilled: empty, or a Scene's template untouched. An unfilled
    /// section is offered Draft rather than Revise, and isn't counted as filled on the index.
    /// </summary>
    public static IReadOnlyCollection<string> UnfilledBodies { get; } = ["", SceneTemplate];

    public static bool IsUnfilled(string body) => UnfilledBodies.Contains(body);

    public static IReadOnlyList<AdventureSection> Create(Guid adventureId, DateTimeOffset now)
    {
        (AdventureSectionKind Kind, string Title, string Body)[] parts =
        [
            (AdventureSectionKind.Overview, "Overview", ""),
            (AdventureSectionKind.SessionClock, "Session clock", ""),
            (AdventureSectionKind.Chapter, "Chapter 1", ""),
            (AdventureSectionKind.Scene, "Scene 1", SceneTemplate),
            (AdventureSectionKind.Scene, "Scene 2", SceneTemplate),
            (AdventureSectionKind.Scene, "Scene 3", SceneTemplate),
            (AdventureSectionKind.QuickReference, "Quick reference", ""),
        ];

        return parts.Select((p, i) => new AdventureSection
        {
            Id = Guid.NewGuid(),
            AdventureId = adventureId,
            Kind = p.Kind,
            Title = p.Title,
            BodyMarkdown = p.Body,
            SortOrder = i + 1,
            UpdatedAt = now,
        }).ToList();
    }
}
