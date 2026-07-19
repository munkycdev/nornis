using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

/// <summary>
/// Turns the shared storyline-development read into the continuity signal: quiet Active
/// storylines (measured in sessions since their last development) and unanchored ones.
/// Pure projection over <see cref="StorylineDevelopmentReader"/> — no AI call, no cost.
/// </summary>
public class StorylineContinuityService : IStorylineContinuityService
{
    private readonly StorylineDevelopmentReader _reader;
    private readonly ContinuityOptions _options;

    public StorylineContinuityService(
        StorylineDevelopmentReader reader,
        IOptions<ContinuityOptions> options)
    {
        _reader = reader;
        _options = options.Value;
    }

    public async Task<AppResult<StorylineContinuityReport>> GetContinuityReportAsync(
        Guid worldId, Guid requestingUserId, WorldRole role, CancellationToken ct)
    {
        var data = await _reader.ReadAsync(worldId, requestingUserId, role, ct);
        return AppResult<StorylineContinuityReport>.Success(BuildReport(data, _options.StaleThresholdSessions));
    }

    /// <summary>
    /// The staleness math, isolated so it can be unit-tested against a hand-built data set
    /// without repositories. "Sessions" are the dated sources that touched at least one
    /// storyline (the same session axis the timeline uses).
    /// </summary>
    internal static StorylineContinuityReport BuildReport(StorylineDevelopmentData data, int staleThreshold)
    {
        // The world's dated session axis: every source that produced a storyline development.
        var sessionSourceIds = data.Developments.Keys
            .Select(k => k.SourceId)
            .Distinct()
            .ToList();
        var sessionDates = sessionSourceIds.Select(id => data.Sources[id].OccurredAt!.Value).ToList();

        var latestSource = sessionSourceIds
            .Select(id => data.Sources[id])
            .OrderByDescending(s => s.OccurredAt!.Value)
            .FirstOrDefault();
        var latest = latestSource is null
            ? null
            : new ContinuitySessionRef(latestSource.Id, latestSource.Title, latestSource.OccurredAt!.Value);

        var active = data.Storylines.Where(s => s.Status == ArtifactStatus.Active).ToList();

        var quiet = new List<QuietStoryline>();
        var unanchored = new List<UnanchoredStoryline>();

        foreach (var s in active)
        {
            var devDates = data.Developments
                .Where(kv => kv.Key.StorylineId == s.Id)
                .Select(kv => data.Sources[kv.Key.SourceId].OccurredAt!.Value)
                .ToList();

            var openQuestionCount = data.Facts.Count(f => f.ArtifactId == s.Id
                && string.Equals(f.Predicate, "open question", StringComparison.OrdinalIgnoreCase)
                && f.TruthState != TruthState.False);

            if (devDates.Count == 0)
            {
                // No dated anchor to measure against — can't judge staleness, so never quiet.
                unanchored.Add(new UnanchoredStoryline(s.Id, s.Name, s.CreatedAt, openQuestionCount));
                continue;
            }

            var lastDev = devDates.Max();
            // Sessions strictly after this storyline's last development are sessions it sat
            // out; the max means no such session touched it.
            var sessionsSince = sessionDates.Count(d => d > lastDev);

            if (sessionsSince >= staleThreshold)
            {
                quiet.Add(new QuietStoryline(
                    s.Id,
                    s.Name,
                    s.Status.ToString(),
                    lastDev,
                    sessionsSince,
                    openQuestionCount,
                    data.ParentByChild.TryGetValue(s.Id, out var parentId) ? parentId : null));
            }
        }

        var orderedQuiet = quiet
            .OrderByDescending(q => q.SessionsSinceLastDevelopment)
            .ThenByDescending(q => q.OpenQuestionCount)
            .ThenBy(q => q.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var orderedUnanchored = unanchored
            .OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new StorylineContinuityReport(active.Count, staleThreshold, latest, orderedQuiet, orderedUnanchored);
    }
}
