namespace Nornis.Domain.Enums;

/// <summary>
/// What an <see cref="Entities.AdventureSection"/> is for. The kind steers drafting and the
/// print layout; it does not constrain the content, which is the GM's.
/// </summary>
public enum AdventureSectionKind
{
    Overview,
    SessionClock,
    Chapter,
    Scene,
    QuickReference,
    Notes,
}
