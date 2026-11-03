using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Prep;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Application.Services;

/// <summary>
/// Prep, without the AI: the GM's Adventures and their sections. Every operation is GM-only
/// and checks the caller's role first, the effective one, so a GM viewing as a player is
/// refused like a player. Controllers carry no role logic of their own.
/// </summary>
public interface IAdventureService
{
    Task<AppResult<IReadOnlyList<AdventureSummary>>> ListAsync(Guid worldId, WorldRole role, CancellationToken ct);

    Task<AppResult<AdventureView>> GetAsync(Guid worldId, Guid adventureId, WorldRole role, CancellationToken ct);

    /// <summary>By slug or id. The role is checked before the key is looked up, so a refusal says nothing about which slugs exist.</summary>
    Task<AppResult<AdventureView>> GetByKeyAsync(Guid worldId, string key, WorldRole role, CancellationToken ct);

    /// <summary>A new Adventure laid out with the house skeleton.</summary>
    Task<AppResult<AdventureView>> CreateAsync(CreateAdventureCommand command, CancellationToken ct);

    /// <summary>A new Adventure split from Markdown files; see <c>AdventureMarkdownImporter</c>.</summary>
    Task<AppResult<AdventureView>> ImportAsync(ImportAdventureCommand command, CancellationToken ct);

    Task<AppResult<Adventure>> UpdateAsync(UpdateAdventureCommand command, CancellationToken ct);

    /// <summary>Replaces the four editable details. 400 <c>invalid_details</c>, with nothing saved, when any is out of bounds.</summary>
    Task<AppResult<AdventureView>> UpdateDetailsAsync(UpdateAdventureDetailsCommand command, CancellationToken ct);

    /// <summary>The GM's edit. 409 when the section changed since <c>BasedOnUpdatedAt</c>.</summary>
    Task<AppResult<AdventureSection>> SaveSectionAsync(SaveSectionCommand command, CancellationToken ct);

    Task<AppResult<AdventureView>> AddSectionAsync(AddSectionCommand command, CancellationToken ct);

    Task<AppResult<AdventureView>> DeleteSectionAsync(DeleteSectionCommand command, CancellationToken ct);

    Task<AppResult<AdventureView>> ReorderAsync(ReorderSectionsCommand command, CancellationToken ct);

    /// <summary>Deletes the Adventure. Its prints and exports stay, with their link to it cleared.</summary>
    Task<AppResult> DeleteAsync(DeleteAdventureCommand command, CancellationToken ct);

    /// <summary>The booklet as a PDF (feature 34), the same one File to Library files.</summary>
    Task<AppResult<PrintedBooklet>> GetBookletAsync(Guid worldId, Guid adventureId, WorldRole role, CancellationToken ct);

    /// <summary>Restores the section's latest revision, the body an AI write replaced, and drops it.</summary>
    Task<AppResult<AdventureSection>> UndoAsync(UndoSectionCommand command, CancellationToken ct);
}
