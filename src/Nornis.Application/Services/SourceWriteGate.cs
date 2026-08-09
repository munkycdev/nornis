using Nornis.Application.Errors;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

/// <summary>
/// The write gate for a source that has not yet entered the extraction pipeline: it exists
/// in this world, the caller owns it or is a GM, and its status still permits change.
///
/// Static and source-in-hand rather than a service that loads: both callers have already
/// loaded the row for their own reasons, and a gate that re-read it would double every
/// write path's queries to restate a rule about the row it was handed.
/// </summary>
public static class SourceWriteGate
{
    /// <summary>
    /// Statuses a source may still be changed in. Anything else means the pipeline has
    /// claimed it — Queued and Processing are mid-flight, Processed has a review batch
    /// whose proposals were written against the content as it stood.
    /// </summary>
    private static readonly SourceProcessingStatus[] MutableStatuses =
        [SourceProcessingStatus.Draft, SourceProcessingStatus.Ready, SourceProcessingStatus.Failed];

    /// <summary>
    /// Returns null when the caller may write to <paramref name="source"/>, or the error
    /// to return. Takes a nullable source so the caller's "not found" and "wrong world"
    /// cases collapse into the same 404 here — telling an unauthorized caller which of the
    /// two it hit would leak that the id exists.
    /// </summary>
    public static AppError? Check(Source? source, Guid worldId, Guid actingUserId, WorldRole role)
    {
        if (role == WorldRole.Observer)
        {
            return new AppError(403, "insufficient_role", "Observers cannot modify sources.");
        }

        if (source is null || source.WorldId != worldId)
        {
            return new AppError(404, "not_found", "Source not found.");
        }

        if (role != WorldRole.GM && source.CreatedByUserId != actingUserId)
        {
            return new AppError(403, "insufficient_role",
                "Only the source's creator or a GM can modify this source.");
        }

        if (!MutableStatuses.Contains(source.ProcessingStatus))
        {
            return new AppError(409, "invalid_status",
                $"This source cannot change while it is {source.ProcessingStatus}.");
        }

        return null;
    }
}
