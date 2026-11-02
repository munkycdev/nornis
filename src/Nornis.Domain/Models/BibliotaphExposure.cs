using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Models;

/// <summary>
/// Which Nornis uploads Bibliotaph may catalogue through its Nornis storage provider (feature
/// 31): stored Sourcebook and Other uploads, and Prep prints. Never another handout, never a
/// map, never a pending upload. The provider endpoints and the outbox both ask this, so what
/// Bibliotaph lists and what it is told changed can't drift apart.
/// </summary>
public static class BibliotaphExposure
{
    public static bool IsExposed(LibraryDocument document) =>
        document.Origin == LibraryDocumentOrigin.Upload
        && document.Status != LibraryDocumentStatus.PendingUpload
        && (document.Kind is LibraryDocumentKind.Sourcebook or LibraryDocumentKind.Other
            || document.AdventureId is not null);
}
