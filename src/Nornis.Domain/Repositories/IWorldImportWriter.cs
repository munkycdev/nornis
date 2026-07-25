using Nornis.Domain.Entities;
using Nornis.Domain.Models;

namespace Nornis.Domain.Repositories;

/// <summary>
/// Persists a fully-materialized imported world in one transaction: the world, its single
/// member, and every content row (already carrying fresh ids and the new WorldId). A failed
/// write leaves no partial world behind. Blob copies happen before this call — orphaned
/// blobs from a failed import are acceptable, matching the upload failure posture elsewhere.
/// </summary>
public interface IWorldImportWriter
{
    Task WriteAsync(World world, WorldMember member, WorldExportData rows, CancellationToken ct = default);
}
