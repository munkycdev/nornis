using Nornis.Domain.Entities;
using Nornis.Domain.Models;

namespace Nornis.Domain.Repositories;

/// <summary>
/// Prep's persistence: an Adventure with its sections and their revisions, as one aggregate.
///
/// Section writes are conditional. Each names the <see cref="AdventureSection.UpdatedAt"/> it
/// was based on and returns false, writing nothing, when the stored row has moved on: a GM's
/// save and an AI draft can't silently overwrite each other. Every section write bumps the
/// section's and the Adventure's <c>UpdatedAt</c>.
/// </summary>
public interface IAdventureRepository
{
    /// <summary>
    /// The world's Adventures, newest work first. A section counts as filled unless its body is
    /// one of <paramref name="unfilledBodies"/>, which the caller supplies because what an
    /// unfilled section looks like is the skeleton's rule, not the database's.
    /// </summary>
    Task<IReadOnlyList<AdventureSummary>> ListByWorldAsync(
        Guid worldId, IReadOnlyCollection<string> unfilledBodies, CancellationToken cancellationToken = default);

    /// <summary>The Adventure with its sections in order, untracked. Null when it doesn't exist.</summary>
    Task<Adventure?> GetWithSectionsAsync(Guid adventureId, CancellationToken cancellationToken = default);

    Task<Adventure> CreateAsync(Adventure adventure, CancellationToken cancellationToken = default);

    /// <summary>Writes the Adventure's own fields (title, pitch, campaign, last print); never its sections.</summary>
    Task UpdateAsync(Adventure adventure, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the section's kind, title and body, with <paramref name="updatedAt"/> as its new
    /// stamp. False, and nothing written, when the stored stamp is not <paramref name="basedOn"/>.
    /// </summary>
    Task<bool> TryUpdateSectionAsync(
        AdventureSection section, DateTimeOffset basedOn, DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the section after <paramref name="afterSectionId"/>, or first when it is null,
    /// and renumbers the Adventure's sections 1..n.
    /// </summary>
    Task InsertSectionAsync(
        AdventureSection section, Guid? afterSectionId, CancellationToken cancellationToken = default);

    /// <summary>Deletes the section and its revisions, and renumbers the rest 1..n.</summary>
    Task DeleteSectionAsync(Guid adventureId, Guid sectionId, CancellationToken cancellationToken = default);

    /// <summary>Numbers the Adventure's sections 1..n in the order given, which names every section exactly once.</summary>
    Task ReorderSectionsAsync(
        Guid adventureId, IReadOnlyList<Guid> orderedSectionIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the Adventure, its sections and their revisions. Exports and prints filed from
    /// it are detached first and survive: each is a snapshot that stands on its own.
    /// </summary>
    Task DeleteAsync(Guid adventureId, CancellationToken cancellationToken = default);

    /// <summary>How many revisions each section of the Adventure holds; sections with none are absent.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountRevisionsAsync(Guid adventureId, CancellationToken cancellationToken = default);

    Task<AdventureSectionRevision?> GetLatestRevisionAsync(Guid sectionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Undo: in one unit of work, writes the revision's body to its section with
    /// <paramref name="updatedAt"/> as the new stamp, and deletes the revision. False, and
    /// nothing written, when the section's stored stamp is not <paramref name="basedOn"/>.
    /// </summary>
    Task<bool> TryRestoreRevisionAsync(
        AdventureSectionRevision revision, DateTimeOffset basedOn, DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);
}
