namespace Nornis.Application.Models;

/// <summary>
/// Result of drafting a fix for a continuity finding. When the model proposed nothing
/// concrete (a valid outcome), <see cref="ProposalCount"/> is 0 and no batch or source is
/// created. Otherwise the proposals sit Pending in the review queue under
/// <see cref="BatchId"/> — nothing changes canon until the GM accepts them.
/// </summary>
public record ContinuityFixDraft(
    Guid? BatchId,
    Guid? SourceId,
    int ProposalCount);
