using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Hero;

namespace Nornis.Api.Controllers;

/// <summary>The top of Home (feature 42), for any member, as their role sees the world.</summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/hero")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class HeroController : ControllerBase
{
    private readonly WorldHeroService _hero;

    public HeroController(WorldHeroService hero)
    {
        _hero = hero;
    }

    [HttpGet]
    public async Task<IActionResult> Get(Guid worldId, CancellationToken ct)
    {
        var result = await _hero.GetAsync(worldId, HttpContext.GetNornisUser().Id, HttpContext.GetWorldMember().Role, ct);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : result.Error!.ToActionResult();
    }

    public static WorldHeroResponse ToResponse(WorldHero hero) => new(
        hero.WorldId, hero.Name, hero.GameSystem, hero.SessionCount, hero.LastSessionAt, hero.EntryCount,
        new HeroPictureResponse(hero.Picture.Kind.ToString(), hero.Picture.KeyArtStamp, hero.Picture.MapUrl, hero.Picture.FocusY));
}
