using System.Linq.Expressions;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Models;

/// <summary>
/// Which Nornis uploads Bibliotaph may catalogue through its Nornis storage provider (feature
/// 31): stored Sourcebook and Other uploads, and Prep prints. Never another handout, never a
/// map, never a pending upload. The provider endpoints and the outbox both ask this, so what
/// Bibliotaph lists and what it is told changed can't drift apart.
///
/// The rule is written once, as an expression, so a query (the provider's file listing) and an
/// in-memory check (the outbox capture) are the same rule rather than two copies of it.
/// </summary>
public static class BibliotaphExposure
{
    public static readonly Expression<Func<LibraryDocument, bool>> Rule = document =>
        document.Origin == LibraryDocumentOrigin.Upload
        && document.Status != LibraryDocumentStatus.PendingUpload
        && (document.Kind == LibraryDocumentKind.Sourcebook
            || document.Kind == LibraryDocumentKind.Other
            || document.AdventureId != null);

    private static readonly Func<LibraryDocument, bool> Compiled = Rule.Compile();

    public static bool IsExposed(LibraryDocument document) => Compiled(document);
}
