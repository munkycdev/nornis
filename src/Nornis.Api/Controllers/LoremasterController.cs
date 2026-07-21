using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Errors;
using Nornis.Application.Knowledge;
using Nornis.Application.Models;
using Nornis.Application.Services;

namespace Nornis.Api.Controllers;

[ApiController]
[Route("api/worlds/{worldId:guid}/ask")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class LoremasterController : ControllerBase
{
    private readonly ILoremasterService _loremasterService;
    private readonly ISuggestionService _suggestionService;

    public LoremasterController(ILoremasterService loremasterService, ISuggestionService suggestionService)
    {
        _loremasterService = loremasterService;
        _suggestionService = suggestionService;
    }

    [HttpGet("suggestions")]
    public async Task<IActionResult> GetSuggestions(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var suggestions = await _suggestionService.GetSuggestionsAsync(
            worldId, user.Id, member.Role, ct);

        return Ok(suggestions.Select(s => new AskSuggestionResponse(s.Text, s.Category)).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Ask(
        Guid worldId,
        [FromBody] AskLoremasterRequest request,
        CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var command = new AskLoremasterCommand(
            WorldId: worldId,
            Question: request.Question,
            UserId: user.Id,
            UserRole: member.Role,
            ConversationContext: request.ConversationContext);

        var result = await _loremasterService.AskAsync(command, ct);

        if (!result.IsSuccess)
            return MapError(result.Error!);

        var answer = result.Value!;
        return Ok(ToAnswerResponse(answer));
    }

    public static AskAnswerResponse ToAnswerResponse(LoremasterAnswer answer)
    {
        return new AskAnswerResponse(
            Answer: answer.AnswerText,
            Citations: answer.Citations.Select(ToCitationResponse).ToList(),
            Confidence: answer.Confidence.ToString(),
            Caveats: answer.Caveats.ToList());
    }

    private static CitationResponse ToCitationResponse(Citation citation)
    {
        return new CitationResponse(
            ReferenceId: citation.ReferenceId,
            Type: citation.Type.ToString(),
            DisplayName: citation.DisplayName,
            ArtifactId: citation.ArtifactId,
            FactId: citation.FactId,
            RelationshipId: citation.RelationshipId,
            SourceId: citation.SourceId,
            DocumentId: citation.DocumentId);
    }

    private IActionResult MapError(AppError error)
    {
        return error.StatusCode switch
        {
            400 => BadRequest(new ErrorResponse(error.Code, error.Message)),
            429 => StatusCode(429, new ErrorResponse(error.Code, error.Message)),
            503 => StatusCode(503, new ErrorResponse(error.Code, error.Message)),
            _ => StatusCode(500, new ErrorResponse("internal_error", "Something went wrong. Please try again."))
        };
    }
}
