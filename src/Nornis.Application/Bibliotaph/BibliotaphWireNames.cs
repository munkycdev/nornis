using Nornis.Domain.Enums;

namespace Nornis.Application.Bibliotaph;

/// <summary>The contract's names for world event kinds; the one map between them and the enum.</summary>
public static class BibliotaphWireNames
{
    public static string Of(BibliotaphWorldEventKind kind) => kind switch
    {
        BibliotaphWorldEventKind.Created => "created",
        BibliotaphWorldEventKind.Renamed => "renamed",
        BibliotaphWorldEventKind.Deleted => "deleted",
        BibliotaphWorldEventKind.FilesChanged => "files-changed",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a world event kind."),
    };
}
