using System.Diagnostics;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
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

        var result = await _loremasterService.BeginAskAsync(command, ct);

        return result.IsSuccess
            ? ToStreamResult(result.Value!)
            : result.Error!.ToActionResult();
    }

    /// <summary>
    /// The answer as server-sent events: <c>start</c> with what is already known, a <c>delta</c>
    /// per piece of text, then <c>done</c> with the whole answer or <c>error</c> if the model call
    /// failed partway. Everything that can refuse the question has run before this is built, so
    /// those refusals keep their status codes; only a failure after the first byte rides in-band.
    /// </summary>
    public static IActionResult ToStreamResult(LoremasterAnswerStream stream) =>
        new ServerSentEventsActionResult(ToServerSentEvents(stream));

    private static async IAsyncEnumerable<SseItem<string>> ToServerSentEvents(
        LoremasterAnswerStream stream,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return Event(AskStreamEventNames.Start,
            new AskStreamStartResponse(stream.Confidence.ToString(), stream.Caveats));

        await foreach (var e in stream.Events.WithCancellation(ct))
        {
            yield return e switch
            {
                LoremasterStreamEvent.Delta delta =>
                    Event(AskStreamEventNames.Delta, new AskStreamDeltaResponse(delta.Text)),
                LoremasterStreamEvent.Completed completed =>
                    Event(AskStreamEventNames.Done, ToAnswerResponse(completed.Answer)),
                LoremasterStreamEvent.Failed failed =>
                    Event(AskStreamEventNames.Error, failed.Error.ToErrorResponse()),
                _ => throw new UnreachableException($"Unhandled Loremaster stream event {e.GetType().Name}."),
            };
        }
    }

    // Serialized here rather than by the SSE result so the payload is the same web-default JSON
    // (camelCase) every other endpoint returns; the result writes a string payload verbatim.
    private static SseItem<string> Event<T>(string eventName, T payload) =>
        new(JsonSerializer.Serialize(payload, JsonSerializerOptions.Web), eventName);

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
            DocumentId: citation.DocumentId,
            Slug: citation.Slug);
    }
}
