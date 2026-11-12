using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Filters;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Enums;

namespace Nornis.Api.Controllers;

/// <summary>
/// The check-in and the world's standing instructions (feature 44). GM-only throughout; the
/// service says so, this only carries the calls.
/// </summary>
[ApiController]
[Route("api/worlds/{worldId:guid}/check-in")]
[ServiceFilter(typeof(WorldMemberActionFilter))]
public class CheckInController : ControllerBase
{
    private readonly ICheckInService _checkInService;

    public CheckInController(ICheckInService checkInService)
    {
        _checkInService = checkInService;
    }

    /// <summary>Whether a check-in is due, the open one, and the standing instructions. No AI.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _checkInService.GetAsync(worldId, user.Id, member.Role, ct);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : result.Error!.ToActionResult();
    }

    /// <summary>Ask me now: one AI call on the daily budget, then a new open check-in.</summary>
    [HttpPost("start")]
    public async Task<IActionResult> Start(Guid worldId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _checkInService.StartAsync(worldId, user.Id, member.Role, ct);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : result.Error!.ToActionResult();
    }

    [HttpPost("questions/{questionId:guid}/answer")]
    public async Task<IActionResult> Answer(
        Guid worldId, Guid questionId, [FromBody] AnswerCheckInQuestionRequest request, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        if (!EnumParsing.TryParseDefined<CheckInAnswer>(request.Answer ?? "", out var answer))
        {
            return BadRequest(new ErrorResponse("invalid_answer", $"'{request.Answer}' isn't one of the answers a question takes."));
        }

        var command = new AnswerCheckInQuestionCommand(
            WorldId: worldId,
            QuestionId: questionId,
            Answer: answer,
            RuleText: request.RuleText,
            RecordOnArtifactId: request.RecordOnArtifactId,
            ActingUserId: user.Id,
            ActingUserRole: member.Role);

        var result = await _checkInService.AnswerAsync(command, ct);
        if (!result.IsSuccess)
        {
            return result.Error!.ToActionResult();
        }

        var value = result.Value!;
        return Ok(new CheckInAnswerResponse(
            ToResponse(value.Question),
            value.Rule is null ? null : ToResponse(value.Rule)));
    }

    [HttpPost("rules")]
    public async Task<IActionResult> AddRule(
        Guid worldId, [FromBody] AddEditorialRuleRequest request, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _checkInService.AddRuleAsync(worldId, request.Text ?? "", user.Id, member.Role, ct);
        return result.IsSuccess ? Ok(ToResponse(result.Value!)) : result.Error!.ToActionResult();
    }

    [HttpDelete("rules/{ruleId:guid}")]
    public async Task<IActionResult> RemoveRule(Guid worldId, Guid ruleId, CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var member = HttpContext.GetWorldMember();

        var result = await _checkInService.RemoveRuleAsync(worldId, ruleId, user.Id, member.Role, ct);
        return result.IsSuccess ? NoContent() : result.Error!.ToActionResult();
    }

    private static CheckInResponse ToResponse(CheckInView view) => new(
        Due: view.Due,
        SessionsSinceLast: view.SessionsSinceLast,
        CadenceSessions: view.CadenceSessions,
        QuestionCount: view.QuestionCount,
        MaxRules: view.MaxRules,
        LastCheckInAt: view.LastCheckInAt,
        Current: view.Current is null
            ? null
            : new CheckInRoundResponse(
                view.Current.Id,
                view.Current.CreatedAt,
                view.Current.Questions.Select(ToResponse).ToList()),
        Rules: view.Rules.Select(ToResponse).ToList());

    private static CheckInQuestionResponse ToResponse(CheckInQuestionView q) => new(
        Id: q.Id,
        Order: q.Order,
        Asked: q.Asked,
        Kind: q.Kind.ToString(),
        SubjectArtifactId: q.SubjectArtifactId,
        SubjectLabel: q.SubjectLabel,
        Text: q.Text,
        Why: q.Why,
        Evidence: new CheckInEvidenceResponse(
            Summary: q.Evidence.Summary,
            Status: q.Evidence.Status,
            FactCount: q.Evidence.FactCount,
            SourceCount: q.Evidence.SourceCount,
            SessionsQuiet: q.Evidence.SessionsQuiet,
            OpenQuestionCount: q.Evidence.OpenQuestionCount,
            FirstSeenTitle: q.Evidence.FirstSeenTitle,
            FirstSeenAt: q.Evidence.FirstSeenAt,
            Facts: q.Evidence.Facts,
            RelatedEntries: q.Evidence.RelatedEntries
                .Select(e => new CheckInRelatedEntryResponse(e.Id, e.Name, e.Type))
                .ToList(),
            Predicate: q.Evidence.Predicate,
            ArtifactCount: q.Evidence.ArtifactCount,
            Examples: q.Evidence.Examples),
        SuggestedRule: q.SuggestedRule,
        SuggestedRecordOnArtifactId: q.SuggestedRecordOnArtifactId,
        AllowedAnswers: q.AllowedAnswers.Select(a => a.ToString()).ToList(),
        Answer: q.Answer?.ToString(),
        AnsweredAt: q.AnsweredAt,
        Outcome: q.Outcome,
        RuleId: q.RuleId);

    private static EditorialRuleResponse ToResponse(EditorialRuleView rule) => new(
        Id: rule.Id,
        Text: rule.Text,
        Origin: rule.Origin.ToString(),
        CheckInQuestionId: rule.CheckInQuestionId,
        QuestionText: rule.QuestionText,
        CreatedAt: rule.CreatedAt);
}
