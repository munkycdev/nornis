using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Enums;

namespace Nornis.Api.Controllers;

/// <summary>
/// Canon is the truth-state view over accepted facts and relationships. Returns a flat,
/// visibility-scoped list of canon entries (newest first); the client groups them into
/// sections. Hidden entries are only returned to GMs.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/canon")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class CanonController : ControllerBase
{
    private readonly ICanonService _canonService;

    public CanonController(ICanonService canonService)
    {
        _canonService = canonService;
    }

    /// <summary>Most entries returned when the caller does not ask for fewer.</summary>
    private const int DefaultLimit = 200;

    /// <summary>
    /// The world's canon, newest first.
    ///
    /// <para><b>This endpoint is capped.</b> Without <c>limit</c> it returns at most
    /// <see cref="DefaultLimit"/> entries, and a larger <c>limit</c> is clamped to it. The
    /// response carries no signal that truncation occurred, so a caller that needs the complete
    /// canon must not assume this returns it.</para>
    ///
    /// <para>To get a few of each kind, make ONE request with <c>factLimit</c> and
    /// <c>relationshipLimit</c>. Two requests with <c>kind</c> would reload the world's artifacts
    /// twice — the artifact load dominates this endpoint's cost.</para>
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get(
        Guid worldId,
        [FromQuery] string? truthState,
        [FromQuery] string? kind,
        [FromQuery] int? limit,
        [FromQuery] int? factLimit,
        [FromQuery] int? relationshipLimit,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        TruthState? truthStateFilter = null;
        if (truthState is not null)
        {
            if (!EnumParsing.TryParseDefined<TruthState>(truthState, out var parsed))
            {
                return BadRequest(new ErrorResponse("invalid_truth_state", $"'{truthState}' is not a valid truth state."));
            }
            truthStateFilter = parsed;
        }

        CanonEntryKind? kindFilter = null;
        if (kind is not null)
        {
            if (!EnumParsing.TryParseDefined<CanonEntryKind>(kind, out var parsedKind))
            {
                return BadRequest(new ErrorResponse("invalid_kind", $"'{kind}' is not a valid canon entry kind."));
            }
            kindFilter = parsedKind;
        }

        foreach (var (name, value) in new[] { ("limit", limit), ("factLimit", factLimit), ("relationshipLimit", relationshipLimit) })
        {
            if (value is <= 0)
            {
                return BadRequest(new ErrorResponse("invalid_limit", $"{name} must be greater than zero."));
            }
        }

        // Defaulted rather than unbounded: this used to return the world's entire canon to a
        // caller that renders six rows, and nothing stops it growing.
        var effectiveLimit = Math.Min(limit ?? DefaultLimit, DefaultLimit);

        var query = new CanonQuery(
            WorldId: worldId,
            ActingUserId: user.Id,
            ActingUserRole: member.Role,
            TruthState: truthStateFilter,
            Kind: kindFilter,
            Limit: effectiveLimit,
            FactLimit: factLimit,
            RelationshipLimit: relationshipLimit);

        var result = await _canonService.GetCanonAsync(query, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        var response = result.Value!.Select(ToCanonEntryResponse).ToList();

        return Ok(response);
    }

    private static CanonEntryResponse ToCanonEntryResponse(CanonEntry entry)
    {
        return new CanonEntryResponse(
            Kind: entry.Kind.ToString(),
            Id: entry.Id,
            ArtifactId: entry.ArtifactId,
            ArtifactName: entry.ArtifactName,
            OtherArtifactId: entry.OtherArtifactId,
            OtherArtifactName: entry.OtherArtifactName,
            Label: entry.Label,
            Detail: entry.Detail,
            Confidence: entry.Confidence,
            TruthState: entry.TruthState.ToString(),
            Visibility: entry.Visibility.ToString(),
            UpdatedAt: entry.UpdatedAt);
    }
}
