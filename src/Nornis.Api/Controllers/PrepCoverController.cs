using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Prep.Covers;
using Nornis.Domain.Enums;

namespace Nornis.Api.Controllers;

/// <summary>
/// An Adventure's cover art (feature 38). GM-only, with the rule in
/// <see cref="AdventureCoverService"/>; this controller only translates.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/prep/{adventureId:guid}/cover")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class PrepCoverController : ControllerBase
{
    private readonly AdventureCoverService _covers;
    private readonly PrepOptions _options;

    public PrepCoverController(AdventureCoverService covers, IOptions<PrepOptions> options)
    {
        _covers = covers;
        _options = options.Value;
    }

    private Guid UserId => HttpContext.GetNornisUser().Id;

    private WorldRole Role => HttpContext.GetWorldMember().Role;

    [HttpGet]
    public async Task<IActionResult> Get(Guid worldId, Guid adventureId, CancellationToken ct) =>
        Respond(await _covers.GetAsync(worldId, adventureId, UserId, Role, ct));

    /// <summary>The Loremaster writes a brief from the title, pitch, Overview and game system.</summary>
    [HttpPost("brief")]
    public async Task<IActionResult> WriteBrief(Guid worldId, Guid adventureId, CancellationToken ct) =>
        Respond(await _covers.WriteBriefAsync(new WriteCoverBriefCommand(worldId, adventureId, UserId, Role), ct));

    /// <summary>The GM's own edit of the brief.</summary>
    [HttpPut("brief")]
    public async Task<IActionResult> SaveBrief(Guid worldId, Guid adventureId, [FromBody] CoverBriefRequest request, CancellationToken ct) =>
        Respond(await _covers.SaveBriefAsync(new SaveCoverBriefCommand(worldId, adventureId, UserId, Role, request.Brief), ct));

    [HttpPost("generate")]
    public async Task<IActionResult> Generate(Guid worldId, Guid adventureId, [FromBody] CoverBriefRequest request, CancellationToken ct) =>
        Respond(await _covers.GenerateAsync(new GenerateCoverCommand(worldId, adventureId, UserId, Role, request.Brief ?? ""), ct));

    [HttpPost("choose")]
    public async Task<IActionResult> Choose(Guid worldId, Guid adventureId, [FromBody] ChooseCoverRequest request, CancellationToken ct) =>
        Respond(await _covers.ChooseAsync(new ChooseCoverCommand(worldId, adventureId, UserId, Role, request.OptionId), ct));

    /// <summary>Refines one option on the refining model (feature 45); the result is a new option.</summary>
    [HttpPost("refine")]
    public async Task<IActionResult> Refine(Guid worldId, Guid adventureId, [FromBody] ChooseCoverRequest request, CancellationToken ct) =>
        Respond(await _covers.RefineAsync(new RefineCoverCommand(worldId, adventureId, UserId, Role, request.OptionId), ct));

    /// <summary>The GM's own art, as the raw request body. Its type is read from its bytes, not the header.</summary>
    [HttpPut("image")]
    public async Task<IActionResult> Upload(Guid worldId, Guid adventureId, CancellationToken ct)
    {
        var bytes = await RequestBodies.ReadBoundedAsync(Request.Body, _options.MaxCoverUploadBytes, ct);
        return Respond(await _covers.UploadAsync(new UploadCoverCommand(worldId, adventureId, UserId, Role, bytes), ct));
    }

    [HttpDelete]
    public async Task<IActionResult> Remove(Guid worldId, Guid adventureId, CancellationToken ct) =>
        Respond(await _covers.RemoveAsync(new RemoveCoverCommand(worldId, adventureId, UserId, Role), ct));

    [HttpPut("layout")]
    public async Task<IActionResult> SetLayout(Guid worldId, Guid adventureId, [FromBody] SetCoverLayoutRequest request, CancellationToken ct)
    {
        if (!EnumParsing.TryParseDefined<CoverLayout>(request.Layout ?? "", out var layout))
        {
            return BadRequest(new ErrorResponse("invalid_layout", $"'{request.Layout}' is not a cover layout: use one of {string.Join(", ", Enum.GetNames<CoverLayout>())}."));
        }

        return Respond(await _covers.SetLayoutAsync(new SetCoverLayoutCommand(worldId, adventureId, UserId, Role, layout), ct));
    }

    /// <summary>The cover's image. The Web proxies it with the image's stamp in its URL, so it may be cached.</summary>
    [HttpGet("image")]
    public async Task<IActionResult> Image(Guid worldId, Guid adventureId, CancellationToken ct) =>
        File(await _covers.OpenCoverAsync(worldId, adventureId, Role, ct));

    /// <summary>One generated option's image; an option never changes, so it may be cached.</summary>
    [HttpGet("options/{optionId:guid}")]
    public async Task<IActionResult> Option(Guid worldId, Guid adventureId, Guid optionId, CancellationToken ct) =>
        File(await _covers.OpenOptionAsync(worldId, adventureId, optionId, Role, ct));

    private IActionResult File(AppResult<CoverImage> result)
    {
        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        Response.Headers.CacheControl = "private, max-age=86400";
        return File(result.Value!.Bytes, result.Value.ContentType);
    }

    private IActionResult Respond(AppResult<CoverState> result) =>
        result.IsSuccess ? Ok(ToResponse(result.Value!)) : result.Error!.ToActionResult();

    public static CoverStateResponse ToResponse(CoverState state) => new(
        state.Cover is { } cover
            ? new CoverResponse(cover.Source.ToString(), cover.Layout.ToString(), cover.ContentType, cover.UpdatedAt)
            : null,
        state.Brief, state.Options, state.ChosenOptionId, state.ImagesGenerated, state.ImagesCap,
        state.CanGenerate, state.GenerateBlocker, state.Warning);
}
