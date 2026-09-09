using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Knowledge;

/// <summary>
/// What the record has that the written sheet does not: items the knowledge graph connects to
/// this character whose names the sheet never mentions. An observation for the player to
/// glance at, never a correction — nothing here writes anything.
///
/// The comparison is a plain case-insensitive presence test of each artifact's name in the
/// sheet's text, and that is the whole of it. It does not parse the sheet, does not infer
/// quantity, ownership or state, and does not know what a name means. Tuning it toward
/// cleverness is how a text search becomes a parser, and a parser is the rules engine this
/// feature exists to avoid: if the plain test proves noisy, the design says delete it.
///
/// Takes the reader's already-filtered record and the sheet as the reader may read it, so it
/// has no way to see anything the reader may not. The sheet arrives as opaque text on purpose;
/// this type never touches the character.
/// </summary>
public static class UnreconciledItems
{
    /// <summary>
    /// The artifact types worth reconciling against a sheet. The user story is "the record
    /// says I picked something up that my sheet never recorded"; nobody writes every location
    /// they have stood in on a character sheet, and reporting them would be the noise the
    /// design says to delete rather than tune.
    /// </summary>
    public static readonly IReadOnlyList<ArtifactType> ObservedTypes = [ArtifactType.Item];

    /// <summary>
    /// Empty when there is no record, no readable sheet, or nothing left over. A sheet that
    /// does not exist has nothing to be reconciled against — "everything is missing from a
    /// blank page" is true and useless — so no sheet means no observation rather than all of
    /// them. Bounded by the record's own per-group cap, since it reads only what the record
    /// chose to show.
    /// </summary>
    public static IReadOnlyList<UnreconciledItem> Find(CharacterRecord? record, string? sheetText)
    {
        if (record is null || string.IsNullOrWhiteSpace(sheetText))
        {
            return [];
        }

        return record.Groups
            .Where(g => ObservedTypes.Contains(g.Type))
            .SelectMany(g => g.Artifacts)
            .Where(a => !sheetText.Contains(a.Name, StringComparison.OrdinalIgnoreCase))
            .Select(a => new UnreconciledItem(a.Id, a.Name, a.Type))
            .ToList();
    }
}
