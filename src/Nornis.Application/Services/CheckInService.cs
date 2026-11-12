using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Common;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// The check-in (feature 44). Composes what exists: the continuity reader for the session axis
/// and the quiet signal, <see cref="CheckInCandidates"/> for what to doubt, one AI call to choose
/// and phrase, the synthetic batch writer for status changes with provenance, and the merge
/// service for folding a storyline into the character it was really about. Nothing here writes
/// to canon outside those two paths.
/// </summary>
public class CheckInService : ICheckInService
{
    /// <summary>Check-ins read when one starts: enough to remember what was answered and every Stop.</summary>
    private const int CheckInsReadOnStart = 24;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly StorylineDevelopmentReader _reader;
    private readonly ContinuityOptions _continuity;
    private readonly CheckInOptions _options;
    private readonly LoremasterOptions _loremaster;
    private readonly IWorldRepository _worldRepository;
    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _factRepository;
    private readonly IArtifactRelationshipRepository _relationshipRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;
    private readonly ISourceRepository _sourceRepository;
    private readonly ICheckInRepository _checkInRepository;
    private readonly IEditorialRuleRepository _ruleRepository;
    private readonly ICheckInAiClient _aiClient;
    private readonly IAiBudgetGuard _budgetGuard;
    private readonly IAiUsageRecorder _usageRecorder;
    private readonly SyntheticBatchWriter _batchWriter;
    private readonly IArtifactMergeService _mergeService;
    private readonly ILogger<CheckInService> _logger;

    public CheckInService(
        StorylineDevelopmentReader reader,
        IOptions<ContinuityOptions> continuityOptions,
        IOptions<CheckInOptions> options,
        IOptions<LoremasterOptions> loremasterOptions,
        IWorldRepository worldRepository,
        IArtifactRepository artifactRepository,
        IArtifactFactRepository factRepository,
        IArtifactRelationshipRepository relationshipRepository,
        ISourceReferenceRepository sourceReferenceRepository,
        ISourceRepository sourceRepository,
        ICheckInRepository checkInRepository,
        IEditorialRuleRepository ruleRepository,
        ICheckInAiClient aiClient,
        IAiBudgetGuard budgetGuard,
        IAiUsageRecorder usageRecorder,
        SyntheticBatchWriter batchWriter,
        IArtifactMergeService mergeService,
        ILogger<CheckInService> logger)
    {
        _reader = reader;
        _continuity = continuityOptions.Value;
        _options = options.Value;
        _loremaster = loremasterOptions.Value;
        _worldRepository = worldRepository;
        _artifactRepository = artifactRepository;
        _factRepository = factRepository;
        _relationshipRepository = relationshipRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
        _sourceRepository = sourceRepository;
        _checkInRepository = checkInRepository;
        _ruleRepository = ruleRepository;
        _aiClient = aiClient;
        _budgetGuard = budgetGuard;
        _usageRecorder = usageRecorder;
        _batchWriter = batchWriter;
        _mergeService = mergeService;
        _logger = logger;
    }

    // ------------------------------------------------------------------------ Read --

    public async Task<AppResult<CheckInView>> GetAsync(
        Guid worldId, Guid requestingUserId, WorldRole role, CancellationToken ct)
    {
        if (GmOnly(role, "open the check-in") is { } refused)
        {
            return AppResult<CheckInView>.Fail(refused);
        }

        var world = await _worldRepository.GetByIdAsync(worldId, ct);
        if (world is null)
        {
            return AppResult<CheckInView>.Fail(NotFound("World"));
        }

        var open = await _checkInRepository.GetOpenByWorldAsync(worldId, ct);
        var last = open ?? (await _checkInRepository.ListRecentByWorldAsync(worldId, 1, ct)).FirstOrDefault();
        var sessions = await ListDatedSessionsAsync(worldId, requestingUserId, role, ct);
        var rules = await _ruleRepository.ListByWorldAsync(worldId, ct);

        return AppResult<CheckInView>.Success(await BuildViewAsync(world, open, last, sessions, rules, ct));
    }

    // ----------------------------------------------------------------------- Start --

    public async Task<AppResult<CheckInView>> StartAsync(
        Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        if (GmOnly(role, "start a check-in") is { } refused)
        {
            return AppResult<CheckInView>.Fail(refused);
        }

        var world = await _worldRepository.GetByIdAsync(worldId, ct);
        if (world is null)
        {
            return AppResult<CheckInView>.Fail(NotFound("World"));
        }

        var budgetError = await _budgetGuard.CheckAsync(worldId, ct);
        if (budgetError is not null)
        {
            return AppResult<CheckInView>.Fail(budgetError);
        }

        var questionCount = QuestionCount(world);
        var sessions = await ListDatedSessionsAsync(worldId, actingUserId, role, ct);
        var rules = await _ruleRepository.ListByWorldAsync(worldId, ct);
        var previous = await _checkInRepository.ListRecentByWorldAsync(worldId, CheckInsReadOnStart, ct);
        var evidence = await GatherEvidenceAsync(worldId, actingUserId, role, sessions, previous, ct);
        var candidates = CheckInCandidates.Build(evidence, _options);

        var now = DateTimeOffset.UtcNow;
        var latestSession = sessions.FirstOrDefault();
        var checkIn = new CheckIn
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            CreatedAt = now,
            CreatedByUserId = actingUserId,
            Model = string.Empty,
            LatestSessionSourceId = latestSession?.Id,
            LatestSessionAt = latestSession?.OccurredAt
        };

        var picks = new List<(CheckInCandidate Candidate, CheckInPick Pick)>();
        if (candidates.Count > 0)
        {
            var ruleTexts = rules.Select(r => r.Text).ToList();
            CheckInAiResponse response;
            try
            {
                response = await _aiClient.ChooseAsync(new AiPromptRequest
                {
                    Scope = AiCallScope.ForWorld(worldId, actingUserId),
                    SystemPrompt = CheckInPrompt.BuildSystemPrompt(questionCount),
                    UserMessage = CheckInPrompt.BuildUserMessage(world.Name, questionCount, ruleTexts, candidates),
                    Model = _loremaster.AiModel,
                    TimeoutSeconds = _options.AiTimeoutSeconds
                }, ct);
            }
            catch (AiRefusalException refusal)
            {
                // Nothing could pay, or the paying key was refused (feature 39): nothing was
                // charged, and the code tells the GM what to fix.
                return AppResult<CheckInView>.Fail(refusal.ToAppError());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Check-in AI call failed. WorldId={WorldId}", worldId);
                await TrackUsageAsync(worldId, actingUserId, null, false, "AiCallFailure", ct);
                return AppResult<CheckInView>.Fail(new AppError(502, "ai_call_failed",
                    "The check-in's AI call failed. Please try again."));
            }

            await TrackUsageAsync(worldId, actingUserId, response, true, null, ct);
            checkIn.Model = response.Usage.Model;
            picks = ResolvePicks(response.Picks, candidates, questionCount);
        }

        checkIn.Questions = BuildQuestions(checkIn, candidates, picks);

        // The previous check-in closes only once the new one is ready to replace it: a failed
        // call above leaves the GM with what they had.
        foreach (var stale in previous.Where(c => c.ClosedAt is null))
        {
            await _checkInRepository.CloseAsync(stale.Id, now, ct);
        }
        await _checkInRepository.CreateAsync(checkIn, ct);

        _logger.LogInformation(
            "Check-in started. WorldId={WorldId}, Candidates={Candidates}, Asked={Asked}, CheckInId={CheckInId}",
            worldId, candidates.Count, picks.Count, checkIn.Id);

        return AppResult<CheckInView>.Success(await BuildViewAsync(world, checkIn, checkIn, sessions, rules, ct));
    }

    /// <summary>
    /// The model's picks, held to what was sent: unknown keys dropped, duplicates collapsed to
    /// the first, more than asked truncated, a record-on id not among the candidate's related
    /// entries nulled, a long suggestion cut. Order is the model's.
    /// </summary>
    internal static List<(CheckInCandidate Candidate, CheckInPick Pick)> ResolvePicks(
        IReadOnlyList<CheckInPick> picks, IReadOnlyList<CheckInCandidate> candidates, int questionCount)
    {
        var byKey = candidates.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolved = new List<(CheckInCandidate, CheckInPick)>();

        foreach (var pick in picks)
        {
            var key = pick.CandidateKey.Trim();
            if (!byKey.TryGetValue(key, out var candidate) || !taken.Add(key))
            {
                continue;
            }

            var recordOn = Guid.TryParse(pick.RecordOnArtifactId, out var id)
                           && candidate.Evidence.RelatedEntries.Any(e => e.Id == id)
                ? id.ToString()
                : null;

            resolved.Add((candidate, new CheckInPick
            {
                CandidateKey = candidate.Key,
                Question = Clean(pick.Question, CheckInQuestion.MaxTextChars) ?? candidate.Text,
                Why = Clean(pick.Why, CheckInQuestion.MaxTextChars) ?? candidate.Why,
                SuggestedRule = Clean(pick.SuggestedRule, EditorialRule.MaxTextChars),
                RecordOnArtifactId = recordOn
            }));

            if (resolved.Count >= questionCount)
            {
                break;
            }
        }

        return resolved;
    }

    private static string? Clean(string? text, int maxChars)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Truncate(maxChars, ellipsis: true);
    }

    private static List<CheckInQuestion> BuildQuestions(
        CheckIn checkIn,
        IReadOnlyList<CheckInCandidate> candidates,
        IReadOnlyList<(CheckInCandidate Candidate, CheckInPick Pick)> picks)
    {
        var questions = new List<CheckInQuestion>();
        var picked = picks.Select(p => p.Candidate.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (candidate, pick) in picks)
        {
            questions.Add(Question(checkIn, candidate, questions.Count, asked: true, pick.Question, pick.Why,
                pick.SuggestedRule, pick.RecordOnArtifactId is { } id ? Guid.Parse(id) : null));
        }

        foreach (var candidate in candidates.Where(c => !picked.Contains(c.Key)))
        {
            questions.Add(Question(checkIn, candidate, questions.Count, asked: false, candidate.Text, candidate.Why, null, null));
        }

        return questions;
    }

    private static CheckInQuestion Question(
        CheckIn checkIn, CheckInCandidate candidate, int order, bool asked,
        string text, string why, string? suggestedRule, Guid? suggestedRecordOn) => new()
        {
            Id = Guid.NewGuid(),
            CheckInId = checkIn.Id,
            Order = order,
            Asked = asked,
            Kind = candidate.Kind,
            SubjectArtifactId = candidate.SubjectArtifactId,
            SubjectLabel = candidate.SubjectLabel.Truncate(CheckInQuestion.MaxLabelChars),
            Text = text,
            Why = string.IsNullOrWhiteSpace(why) ? null : why,
            EvidenceJson = JsonSerializer.Serialize(candidate.Evidence, JsonOptions),
            SuggestedRule = suggestedRule,
            SuggestedRecordOnArtifactId = suggestedRecordOn
        };

    // -------------------------------------------------------------------- Evidence --

    private async Task<CheckInEvidence> GatherEvidenceAsync(
        Guid worldId, Guid actingUserId, WorldRole role, IReadOnlyList<Source> sessions,
        IReadOnlyList<CheckIn> previous, CancellationToken ct)
    {
        // The reader and the continuity math give the quiet signal at GM visibility.
        var developments = await _reader.ReadAsync(worldId, actingUserId, role, ct);
        var continuity = StorylineContinuityService.BuildReport(developments, _continuity.StaleThresholdSessions);
        var quiet = continuity.Quiet.ToDictionary(q => q.StorylineId, q => q.SessionsSinceLastDevelopment);

        var artifacts = (await _artifactRepository.ListByWorldAsync(worldId, null, ct))
            .Where(a => a.Status != ArtifactStatus.Archived)
            .ToList();
        var artifactIds = artifacts.Select(a => a.Id).ToList();
        var facts = await _factRepository.ListByArtifactIdsAsync(artifactIds, VisibilityFilter.All, int.MaxValue, ct);
        var relationships = (await _relationshipRepository.ListByArtifactIdsAsync(artifactIds, VisibilityFilter.All, ct))
            .DistinctBy(r => r.Id)
            .ToList();
        var references = await _sourceReferenceRepository.ListByWorldAsync(worldId, ct);
        var sources = await _sourceRepository.ListByWorldAsync(worldId, ct);

        var remembered = previous.Take(_options.RecentCheckInsRemembered).ToList();
        var recentlyAnswered = remembered
            .SelectMany(c => c.Questions)
            .Where(q => q.IsAnswered && q.SubjectArtifactId is not null)
            .Select(q => q.SubjectArtifactId!.Value)
            .ToHashSet();
        var settledPredicates = remembered
            .SelectMany(c => c.Questions)
            .Where(q => q.Kind == CheckInQuestionKind.Pattern && q.IsAnswered)
            .Select(q => CheckInCandidates.PredicateKey(q.SubjectLabel))
            .Concat(previous
                .SelectMany(c => c.Questions)
                .Where(q => q.Kind == CheckInQuestionKind.Pattern && q.Answer == CheckInAnswer.Stop)
                .Select(q => CheckInCandidates.PredicateKey(q.SubjectLabel)))
            .ToHashSet(StringComparer.Ordinal);

        return new CheckInEvidence(
            artifacts, facts, relationships, references, sources, sessions, quiet, recentlyAnswered, settledPredicates);
    }

    /// <summary>Dated sessions the GM can see, newest first: the cadence axis.</summary>
    private async Task<IReadOnlyList<Source>> ListDatedSessionsAsync(
        Guid worldId, Guid userId, WorldRole role, CancellationToken ct)
    {
        var canSee = SourceVisibilityRule.Compile(userId, role);
        return (await _sourceRepository.ListByWorldAsync(worldId, ct))
            .Where(s => s.OccurredAt is not null && s.ProcessingStatus == SourceProcessingStatus.Processed && canSee(s))
            .OrderByDescending(s => s.OccurredAt!.Value)
            .ToList();
    }

    // ---------------------------------------------------------------------- Answer --

    public async Task<AppResult<CheckInAnswerResult>> AnswerAsync(AnswerCheckInQuestionCommand command, CancellationToken ct)
    {
        if (GmOnly(command.ActingUserRole, "answer a check-in") is { } refused)
        {
            return AppResult<CheckInAnswerResult>.Fail(refused);
        }

        var question = await _checkInRepository.GetQuestionAsync(command.QuestionId, ct);
        if (question is null || question.CheckIn.WorldId != command.WorldId)
        {
            return AppResult<CheckInAnswerResult>.Fail(NotFound("Question"));
        }
        if (question.CheckIn.ClosedAt is not null)
        {
            return AppResult<CheckInAnswerResult>.Fail(new AppError(409, "check_in_closed",
                "A later check-in replaced this one; its questions can no longer be answered."));
        }
        if (question.IsAnswered)
        {
            return AppResult<CheckInAnswerResult>.Fail(new AppError(409, "already_answered",
                "This question was already answered."));
        }
        if (!CheckInAnswers.IsAllowed(question.Kind, command.Answer))
        {
            return AppResult<CheckInAnswerResult>.Fail(new AppError(400, "invalid_answer",
                $"'{command.Answer}' is not an answer a {question.Kind} question takes."));
        }

        // The rule's text and the room for it are settled before anything acts, so an answer
        // that cannot write its instruction does nothing at all.
        var ruleText = command.RuleText?.Trim();
        if (string.IsNullOrEmpty(ruleText) && question.Kind == CheckInQuestionKind.Pattern && command.Answer == CheckInAnswer.Stop)
        {
            // Stop without an instruction would do nothing; the instruction is the whole act.
            ruleText = $"Do not record \"{question.SubjectLabel}\" facts; they are incidental.";
        }
        if (command.Answer == CheckInAnswer.Skip)
        {
            ruleText = null;
        }
        if (ruleText is not null)
        {
            if (ruleText.Length > EditorialRule.MaxTextChars)
            {
                return AppResult<CheckInAnswerResult>.Fail(RuleTooLong());
            }
            if (await BriefFullAsync(command.WorldId, ct) is { } full)
            {
                return AppResult<CheckInAnswerResult>.Fail(full);
            }
        }

        // The action, by kind and answer. A failure here leaves the question unanswered.
        var evidence = Deserialize(question.EvidenceJson);
        Artifact? recordedOn = null;
        Guid? batchId = null;
        if (question.Kind == CheckInQuestionKind.Storyline && command.Answer == CheckInAnswer.Background
            && command.RecordOnArtifactId is { } targetId)
        {
            var target = await _artifactRepository.GetByIdAsync(targetId, ct);
            if (target is null || target.WorldId != command.WorldId || target.Status == ArtifactStatus.Archived)
            {
                return AppResult<CheckInAnswerResult>.Fail(new AppError(400, "invalid_record_on",
                    "The entry to record this on was not found in this world."));
            }

            var merged = await _mergeService.MergeAsync(
                command.WorldId, question.SubjectArtifactId!.Value, target.Id, command.ActingUserId, command.ActingUserRole, ct);
            if (!merged.IsSuccess)
            {
                return AppResult<CheckInAnswerResult>.Fail(merged.Error!);
            }
            recordedOn = target;
        }
        else if (CheckInAnswers.StatusFor(question.Kind, command.Answer) is { } status)
        {
            var changed = await SetStatusAsync(command, question, status, ct);
            if (!changed.IsSuccess)
            {
                return AppResult<CheckInAnswerResult>.Fail(changed.Error!);
            }
            batchId = changed.Value == Guid.Empty ? null : changed.Value;
        }

        // Stamped before the rule is written, so a failure there cannot make a retry redo the act.
        var now = DateTimeOffset.UtcNow;
        question.Answer = command.Answer;
        question.AnsweredAt = now;
        question.AnsweredByUserId = command.ActingUserId;
        question.ActionBatchId = batchId;
        question.RecordedOnArtifactId = recordedOn?.Id;
        await _checkInRepository.UpdateQuestionAsync(question, ct);

        EditorialRule? rule = null;
        if (ruleText is not null)
        {
            rule = await _ruleRepository.CreateAsync(new EditorialRule
            {
                Id = Guid.NewGuid(),
                WorldId = command.WorldId,
                Text = ruleText,
                Origin = EditorialRuleOrigin.CheckIn,
                CheckInQuestionId = question.Id,
                CreatedAt = now,
                CreatedByUserId = command.ActingUserId
            }, ct);
            question.RuleId = rule.Id;
            await _checkInRepository.UpdateQuestionAsync(question, ct);
        }

        _logger.LogInformation(
            "Check-in question answered. WorldId={WorldId}, QuestionId={QuestionId}, Kind={Kind}, Answer={Answer}, BatchId={BatchId}, RecordedOn={RecordedOn}, RuleId={RuleId}",
            command.WorldId, question.Id, question.Kind, command.Answer, batchId, recordedOn?.Id, rule?.Id);

        var view = ToView(question, evidence, recordedOn is null ? [] : new Dictionary<Guid, string> { [recordedOn.Id] = recordedOn.Name });
        return AppResult<CheckInAnswerResult>.Success(new CheckInAnswerResult(
            view, rule is null ? null : ToView(rule, question.Text)));
    }

    /// <summary>
    /// A status change as an accepted proposal on a CheckIn batch, so it has the provenance and
    /// the undo of any reviewed change. Guid.Empty means the subject was already there and
    /// nothing was written (the wrap-up's retry rule).
    /// </summary>
    private async Task<AppResult<Guid>> SetStatusAsync(
        AnswerCheckInQuestionCommand command, CheckInQuestion question, ArtifactStatus status, CancellationToken ct)
    {
        var subject = await _artifactRepository.GetByIdAsync(question.SubjectArtifactId ?? Guid.Empty, ct);
        if (subject is null || subject.WorldId != command.WorldId)
        {
            return AppResult<Guid>.Fail(NotFound("The question's subject"));
        }
        if (subject.Status == status)
        {
            return AppResult<Guid>.Success(Guid.Empty);
        }

        var body = new StringBuilder();
        body.AppendLine(question.Text);
        body.AppendLine($"Answer: {command.Answer}");
        body.AppendLine($"- {subject.Name} → {status}");

        // GM-gated above, so the accepted shape's unrestricted resolution is safe.
        var written = await _batchWriter.WriteAcceptedAsync(
            new SyntheticSourceSpec
            {
                WorldId = command.WorldId,
                ActingUserId = command.ActingUserId,
                Title = $"Check-in — {DateTimeOffset.UtcNow:yyyy-MM-dd}",
                Body = body.ToString()
            },
            ReviewBatchKinds.CheckIn,
            [
                new SyntheticProposalSpec
                {
                    ChangeType = ReviewChangeType.UpdateArtifact,
                    TargetType = ReviewTargetType.Artifact,
                    TargetId = subject.Id,
                    ProposedValueJson = $$"""{"status":"{{status}}"}""",
                    Rationale = $"GM answered the check-in: {command.Answer}."
                }
            ],
            ct);

        return written.IsSuccess
            ? AppResult<Guid>.Success(written.Value!.BatchId)
            : AppResult<Guid>.Fail(written.Error!);
    }

    // ----------------------------------------------------------------------- Rules --

    public async Task<AppResult<EditorialRuleView>> AddRuleAsync(
        Guid worldId, string text, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        if (GmOnly(role, "edit the standing instructions") is { } refused)
        {
            return AppResult<EditorialRuleView>.Fail(refused);
        }

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return AppResult<EditorialRuleView>.Fail(new AppError(400, "validation_error", "An instruction needs some text."));
        }
        if (trimmed.Length > EditorialRule.MaxTextChars)
        {
            return AppResult<EditorialRuleView>.Fail(RuleTooLong());
        }
        if (await BriefFullAsync(worldId, ct) is { } full)
        {
            return AppResult<EditorialRuleView>.Fail(full);
        }

        var rule = await _ruleRepository.CreateAsync(new EditorialRule
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Text = trimmed,
            Origin = EditorialRuleOrigin.Manual,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = actingUserId
        }, ct);

        return AppResult<EditorialRuleView>.Success(ToView(rule, null));
    }

    public async Task<AppResult> RemoveRuleAsync(Guid worldId, Guid ruleId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        if (GmOnly(role, "edit the standing instructions") is { } refused)
        {
            return AppResult.Fail(refused);
        }

        var rule = await _ruleRepository.GetByIdAsync(ruleId, ct);
        if (rule is not null && rule.WorldId != worldId)
        {
            return AppResult.Fail(NotFound("Instruction"));
        }
        if (rule is not null)
        {
            await _ruleRepository.DeleteAsync(rule.Id, ct);
        }

        return AppResult.Success();
    }

    private async Task<AppError?> BriefFullAsync(Guid worldId, CancellationToken ct)
    {
        var count = (await _ruleRepository.ListByWorldAsync(worldId, ct)).Count;
        return count >= _options.MaxRules
            ? new AppError(409, "brief_full",
                $"This world already holds {_options.MaxRules} standing instructions. Remove one first.")
            : null;
    }

    private static AppError RuleTooLong() => new(400, "validation_error",
        $"An instruction is at most {EditorialRule.MaxTextChars} characters.");

    // ------------------------------------------------------------------------ View --

    private async Task<CheckInView> BuildViewAsync(
        World world, CheckIn? open, CheckIn? last, IReadOnlyList<Source> sessions,
        IReadOnlyList<EditorialRule> rules, CancellationToken ct)
    {
        var sessionsSince = CheckInCadence.SessionsSince(sessions.Select(s => s.OccurredAt!.Value), last?.LatestSessionAt);

        CheckInRound? round = null;
        if (open is not null)
        {
            var questions = open.Questions.OrderBy(q => q.Order).ToList();
            var recordedOnIds = questions.Where(q => q.RecordedOnArtifactId is not null).Select(q => q.RecordedOnArtifactId!.Value).Distinct().ToList();
            var names = recordedOnIds.Count == 0
                ? new Dictionary<Guid, string>()
                : (await _artifactRepository.ListByIdsAsync(recordedOnIds, ct)).ToDictionary(a => a.Id, a => a.Name);
            round = new CheckInRound(open.Id, open.CreatedAt,
                questions.Select(q => ToView(q, Deserialize(q.EvidenceJson), names)).ToList());
        }

        var questionTextById = open?.Questions.ToDictionary(q => q.Id, q => q.Text) ?? new Dictionary<Guid, string>();

        return new CheckInView(
            Due: CheckInCadence.IsDue(sessionsSince, _options.CadenceSessions),
            SessionsSinceLast: sessionsSince,
            CadenceSessions: _options.CadenceSessions,
            QuestionCount: QuestionCount(world),
            MaxRules: _options.MaxRules,
            LastCheckInAt: last?.CreatedAt,
            Current: round,
            Rules: rules.Select(r => ToView(r, r.CheckInQuestionId is { } qid ? questionTextById.GetValueOrDefault(qid) : null)).ToList());
    }

    private static CheckInQuestionView ToView(CheckInQuestion q, CheckInEvidenceBlock evidence, IReadOnlyDictionary<Guid, string> recordedOnNames) =>
        new(
            q.Id, q.Order, q.Asked, q.Kind, q.SubjectArtifactId, q.SubjectLabel, q.Text, q.Why, evidence,
            q.SuggestedRule, q.SuggestedRecordOnArtifactId,
            CheckInAnswers.AllowedFor(q.Kind),
            q.Answer, q.AnsweredAt,
            Outcome(q, recordedOnNames),
            q.RuleId);

    /// <summary>What the answer did, in the words the page shows under an answered question.</summary>
    internal static string? Outcome(CheckInQuestion q, IReadOnlyDictionary<Guid, string> recordedOnNames)
    {
        if (q.Answer is not { } answer)
        {
            return null;
        }

        var what = (q.Kind, answer) switch
        {
            (_, CheckInAnswer.Skip) => "Skipped",
            (CheckInQuestionKind.Storyline, CheckInAnswer.Pursuing) => "Pursuing",
            (CheckInQuestionKind.Storyline, CheckInAnswer.Background) when q.RecordedOnArtifactId is { } id =>
                $"Merged into {recordedOnNames.GetValueOrDefault(id, "the entry")}",
            (CheckInQuestionKind.Storyline, CheckInAnswer.Background) => "Set Dormant",
            (CheckInQuestionKind.Storyline, CheckInAnswer.Done) => "Set Resolved",
            (CheckInQuestionKind.Entry, CheckInAnswer.Archive) => "Archived",
            (CheckInQuestionKind.Pattern, CheckInAnswer.Stop) => "Stopped recording",
            (_, CheckInAnswer.Keep) => "Kept",
            _ => answer.ToString(),
        };

        return q.RuleId is null ? what : what + " · instruction added";
    }

    private static EditorialRuleView ToView(EditorialRule r, string? questionText) =>
        new(r.Id, r.Text, r.Origin, r.CheckInQuestionId, questionText, r.CreatedAt);

    private static CheckInEvidenceBlock Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<CheckInEvidenceBlock>(json, JsonOptions) ?? new CheckInEvidenceBlock();
        }
        catch (JsonException)
        {
            return new CheckInEvidenceBlock();
        }
    }

    private int QuestionCount(World world) =>
        Math.Clamp(world.CheckInQuestionCount, 1, World.MaxCheckInQuestionCount);

    private static AppError? GmOnly(WorldRole role, string verb) =>
        role == WorldRole.GM ? null : new AppError(403, "insufficient_role", $"Only GMs can {verb}.");

    private static AppError NotFound(string what) => new(404, "not_found", $"{what} not found.");

    private Task TrackUsageAsync(
        Guid worldId, Guid userId, CheckInAiResponse? response, bool succeeded, string? errorCode, CancellationToken ct) =>
        _usageRecorder.RecordAsync(
            worldId, userId, AiOperationType.CheckIn, response?.Usage,
            succeeded, errorCode, fallbackModel: _loremaster.AiModel, ct: ct);
}
