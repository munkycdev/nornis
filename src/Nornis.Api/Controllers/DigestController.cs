using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Services;

namespace Nornis.Api.Controllers;

/// <summary>
/// World digest — the maintained world-level synthesis (active storylines, recent movements,
/// open questions), served from its persisted read-model and regenerated on GM demand.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/digest")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class DigestController : ControllerBase
{
    private readonly IWorldDigestService _digestService;

    public DigestController(IWorldDigestService digestService)
    {
        _digestService = digestService;
    }

    /// <summary>Returns the stored digest rendered for the caller's role. Any member.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(Guid worldId, CancellationToken ct)
    {
        var member = HttpContext.GetWorldMember();
        var result = await _digestService.GetAsync(worldId, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return Ok(ToResponse(result.Value!));
    }

    /// <summary>Regenerates both renderings. GM-only; two AI calls, ~30-60s.</summary>
    [HttpPost("generate")]
    public async Task<IActionResult> Generate(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();
        var result = await _digestService.GenerateAsync(worldId, user.Id, member.Role, ct);

        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        return Ok(ToResponse(result.Value!));
    }

    private static WorldDigestResponse ToResponse(WorldDigestView v) =>
        new(v.HasData, v.GeneratedAt, v.Content, v.PartyPreview);
}
