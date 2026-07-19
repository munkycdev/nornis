using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface IStorylineWrapUpService
{
    /// <summary>
    /// Assembles the GM session wrap-up: recent advances, quiet storylines, pending lineage
    /// suggestions, and unparented recent arcs. GM-only. Read-only.
    /// </summary>
    Task<AppResult<WrapUpView>> GetWrapUpAsync(
        Guid worldId, Guid requestingUserId, WorldRole role, CancellationToken ct);

    /// <summary>
    /// Applies the GM's wrap-up decisions: closures (as accepted UpdateArtifact proposals with
    /// provenance), lineage accept/reject (via the review flow), and parent assignments. GM-only.
    /// </summary>
    Task<AppResult<WrapUpResult>> ApplyAsync(WrapUpDecisionsCommand command, CancellationToken ct);
}
