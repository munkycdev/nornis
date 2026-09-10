using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;

namespace Nornis.Application.Services;

/// <summary>
/// GM-chosen pages of a Library document become a <see cref="Domain.Enums.SourceType.LibraryExcerpt"/>
/// source and go through extraction like any other. The choosing is the gate: a document as
/// a whole is never extracted.
/// </summary>
public interface ILibraryExcerptService
{
    Task<AppResult<IReadOnlyList<LibraryExcerptCandidate>>> SearchAsync(SearchLibraryExcerptsCommand command, CancellationToken ct);

    /// <summary>Creates the excerpt source and queues it for extraction; the returned source is Queued.</summary>
    Task<AppResult<Source>> FileAsync(FileLibraryExcerptCommand command, CancellationToken ct);
}
