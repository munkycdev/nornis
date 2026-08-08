using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <param name="CreateMissingArtifact">
/// The reviewer has already been shown the artifacts that looked like the one this proposal
/// names, and said none of them is it. Create the named artifact and attach to that instead
/// of asking again. Without it, an accept only creates a missing artifact when nothing in the
/// world resembles the name — the ambiguous case is the reviewer's call, not the pipeline's.
/// </param>
public record AcceptProposalCommand(
    Guid ProposalId,
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    bool CreateMissingArtifact = false);
