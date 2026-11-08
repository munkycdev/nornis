using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Hero;
using Nornis.Application.KeyArt;
using Nornis.Domain.Enums;

namespace Nornis.Api.Controllers;

/// <summary>
/// A world's key art (feature 42). Every write and the card's state are the GM's; the image is
/// any member's, since key art is party-visible. The rules are in <see cref="WorldKeyArtService"/>;
/// this controller only translates.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/key-art")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class KeyArtController : ControllerBase
{
    private readonly WorldKeyArtService _keyArt;
    private readonly KeyArtOptions _options;

    public KeyArtController(WorldKeyArtService keyArt, IOptions<KeyArtOptions> options)
    {
        _keyArt = keyArt;
        _options = options.Value;
    }

    private Guid UserId => HttpContext.GetNornisUser().Id;

    private WorldRole Role => HttpContext.GetWorldMember().Role;

    [HttpGet]
    public async Task<IActionResult> Get(Guid worldId, CancellationToken ct) =>
        Respond(await _keyArt.GetAsync(worldId, UserId, Role, ct));

    [HttpPut("brief")]
    public async Task<IActionResult> SaveBrief(Guid worldId, [FromBody] KeyArtBriefRequest request, CancellationToken ct) =>
        Respond(await _keyArt.SaveBriefAsync(new SaveKeyArtBriefCommand(worldId, UserId, Role, request.Brief), ct));

    [HttpPost("generate")]
    public async Task<IActionResult> Generate(Guid worldId, [FromBody] KeyArtBriefRequest request, CancellationToken ct) =>
        Respond(await _keyArt.GenerateAsync(new GenerateKeyArtCommand(worldId, UserId, Role, request.Brief ?? ""), ct));

    [HttpPost("choose")]
    public async Task<IActionResult> Choose(Guid worldId, [FromBody] ChooseKeyArtRequest request, CancellationToken ct) =>
        Respond(await _keyArt.ChooseAsync(new ChooseKeyArtCommand(worldId, UserId, Role, request.OptionId), ct));

    /// <summary>The GM's own picture, as the raw request body. Its type is read from its bytes, not the header.</summary>
    [HttpPut("image")]
    public async Task<IActionResult> Upload(Guid worldId, CancellationToken ct)
    {
        var bytes = await RequestBodies.ReadBoundedAsync(Request.Body, _options.MaxUploadBytes, ct);
        return Respond(await _keyArt.UploadAsync(new UploadKeyArtCommand(worldId, UserId, Role, bytes), ct));
    }

    [HttpPut("anchor")]
    public async Task<IActionResult> SetAnchor(Guid worldId, [FromBody] SetKeyArtAnchorRequest request, CancellationToken ct)
    {
        if (!EnumParsing.TryParseDefined<KeyArtAnchor>(request.Anchor ?? "", out var anchor))
        {
            return BadRequest(new ErrorResponse("invalid_anchor", $"'{request.Anchor}' is not Centre, Top or Bottom."));
        }

        return Respond(await _keyArt.SetAnchorAsync(new SetKeyArtAnchorCommand(worldId, UserId, Role, anchor), ct));
    }

    [HttpDelete]
    public async Task<IActionResult> Remove(Guid worldId, CancellationToken ct) =>
        Respond(await _keyArt.RemoveAsync(new RemoveKeyArtCommand(worldId, UserId, Role), ct));

    /// <summary>The key art itself, for any member. The Web proxies it with the art's stamp in its URL, so it may be cached.</summary>
    [HttpGet("image")]
    public async Task<IActionResult> Image(Guid worldId, CancellationToken ct) =>
        ImageResult(this, await _keyArt.OpenAsync(worldId, ct));

    /// <summary>One generated option, for the GM's card; an option never changes, so it may be cached.</summary>
    [HttpGet("options/{optionId:guid}")]
    public async Task<IActionResult> Option(Guid worldId, Guid optionId, CancellationToken ct) =>
        ImageResult(this, await _keyArt.OpenOptionAsync(worldId, optionId, Role, ct));

    /// <summary>An image answer, private to the caller's browser for a day: its URL changes when the art does.</summary>
    public static IActionResult ImageResult(ControllerBase controller, AppResult<KeyArtImage> result, bool shared = false)
    {
        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        controller.Response.Headers.CacheControl = shared ? "public, max-age=86400" : "private, max-age=86400";
        return controller.File(result.Value!.Bytes, result.Value.ContentType);
    }

    private IActionResult Respond(AppResult<KeyArtState> result) =>
        result.IsSuccess ? Ok(ToResponse(result.Value!)) : result.Error!.ToActionResult();

    public static KeyArtStateResponse ToResponse(KeyArtState state) => new(
        state.KeyArt is { } art
            ? new KeyArtResponse(art.Source.ToString(), art.Anchor.ToString(), art.ContentType, art.UpdatedAt, WorldHeroService.FocusY(art.Anchor))
            : null,
        state.Brief, state.Options, state.ChosenOptionId, state.ImagesThisMonth, state.ImagesCap,
        state.CanGenerate, state.GenerateBlocker, state.Warning);
}
