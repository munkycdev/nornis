namespace Nornis.Domain.Enums;

/// <summary>
/// What a world event tells Bibliotaph. The wire names (<c>created</c>, <c>renamed</c>,
/// <c>deleted</c>, <c>files-changed</c>) are the contract's and are mapped in one place,
/// <c>BibliotaphWireNames</c>; the stored names are these.
/// </summary>
public enum BibliotaphWorldEventKind
{
    Created,
    Renamed,
    Deleted,
    FilesChanged,
}
