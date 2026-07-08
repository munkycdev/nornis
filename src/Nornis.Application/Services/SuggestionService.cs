using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Builds ask suggestions from live campaign data using deterministic templates — no AI
/// call, no cost. Candidates are grouped by category; one suggestion is taken per category
/// in priority order (storyline, character, rumor, world, recap) and padded with evergreen
/// fallbacks so callers always receive exactly four. A day-based seed rotates the choices
/// so they change over time without flickering between renders.
/// </summary>
public class SuggestionService : ISuggestionService
{
    public const int SuggestionCount = 4;

    private const int RecentArtifactWindow = 20;
    private const int MaxFactsPerArtifact = 10;
    private const int RecentSourceWindowDays = 14;

    private static readonly string[] Fallbacks =
    [
        "What should I remember before tonight?",
        "What storylines are unresolved?",
        "Who have we met recently?",
        "What loose ends should we tie up?",
    ];

    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _artifactFactRepository;
    private readonly ISourceRepository _sourceRepository;

    public SuggestionService(
        IArtifactRepository artifactRepository,
        IArtifactFactRepository artifactFactRepository,
        ISourceRepository sourceRepository)
    {
        _artifactRepository = artifactRepository;
        _artifactFactRepository = artifactFactRepository;
        _sourceRepository = sourceRepository;
    }

    public async Task<IReadOnlyList<AskSuggestion>> GetSuggestionsAsync(
        Guid campaignId,
        Guid userId,
        CampaignRole role,
        CancellationToken ct)
    {
        var allowedScopes = GetAllowedScopes(role);
        var seed = DaySeed(campaignId);

        var recent = await _artifactRepository.ListRecentByCampaignAsync(
            campaignId, allowedScopes, RecentArtifactWindow, ct);

        // One candidate pool per category, in selection priority order.
        var pools = new List<(string Category, List<string> Candidates)>
        {
            ("storyline", StorylineCandidates(recent)),
            ("character", CharacterCandidates(recent)),
            ("rumor", await RumorCandidatesAsync(recent, role, allowedScopes, ct)),
            ("world", WorldCandidates(recent)),
            ("recap", await RecapCandidatesAsync(campaignId, userId, role, ct)),
        };

        var suggestions = new List<AskSuggestion>(SuggestionCount);
        var categoryIndex = 0;
        foreach (var (category, candidates) in pools)
        {
            if (suggestions.Count == SuggestionCount)
                break;

            if (candidates.Count > 0)
            {
                // Offset by category position so one seed doesn't pick the same slot everywhere.
                var pick = candidates[(int)((seed + (uint)categoryIndex) % (uint)candidates.Count)];
                suggestions.Add(new AskSuggestion(pick, category));
            }

            categoryIndex++;
        }

        foreach (var fallback in Fallbacks)
        {
            if (suggestions.Count == SuggestionCount)
                break;

            if (suggestions.All(s => s.Text != fallback))
                suggestions.Add(new AskSuggestion(fallback, "recap"));
        }

        return suggestions;
    }

    private static List<string> StorylineCandidates(IReadOnlyList<Artifact> recent)
    {
        var candidates = new List<string>();

        foreach (var storyline in recent.Where(a => a.Type == ArtifactType.Storyline))
        {
            switch (storyline.Status)
            {
                case ArtifactStatus.Active:
                    candidates.Add($"Where does {storyline.Name} stand?");
                    candidates.Add($"What do we know about {storyline.Name}?");
                    break;
                case ArtifactStatus.Dormant:
                    candidates.Add($"What was left unresolved in {storyline.Name}?");
                    break;
            }
        }

        return candidates;
    }

    private static List<string> CharacterCandidates(IReadOnlyList<Artifact> recent)
    {
        var candidates = new List<string>();

        foreach (var character in recent.Where(a => a.Type == ArtifactType.Character).Take(3))
        {
            candidates.Add($"What has {character.Name} been up to?");
            candidates.Add($"What does the record say about {character.Name}?");
        }

        return candidates;
    }

    private static List<string> WorldCandidates(IReadOnlyList<Artifact> recent)
    {
        return recent
            .Where(a => a.Type is ArtifactType.Location or ArtifactType.Item or ArtifactType.Faction)
            .Take(3)
            .Select(a => $"What do we know about {a.Name}?")
            .ToList();
    }

    private async Task<List<string>> RumorCandidatesAsync(
        IReadOnlyList<Artifact> recent,
        CampaignRole role,
        IReadOnlyList<VisibilityScope> allowedScopes,
        CancellationToken ct)
    {
        if (recent.Count == 0)
            return [];

        var facts = await _artifactFactRepository.ListByArtifactIdsAsync(
            recent.Select(a => a.Id).ToList(), MaxFactsPerArtifact, ct);

        var isGm = role == CampaignRole.GM;
        var hasUnconfirmed = facts.Any(f =>
            allowedScopes.Contains(f.Visibility) &&
            (isGm || f.TruthState != TruthState.Hidden) &&
            f.TruthState is TruthState.Rumor or TruthState.Disputed);

        return hasUnconfirmed
            ? ["What rumors are still unconfirmed?", "Which claims should we treat with suspicion?"]
            : [];
    }

    private async Task<List<string>> RecapCandidatesAsync(
        Guid campaignId, Guid userId, CampaignRole role, CancellationToken ct)
    {
        var sources = await _sourceRepository.ListByCampaignAsync(campaignId, null, ct);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-RecentSourceWindowDays);

        var latest = sources
            .Where(s => s.CreatedAt >= cutoff)
            .Where(s => IsSourceVisible(s, userId, role))
            .MaxBy(s => s.CreatedAt);

        return latest is null ? [] : [$"What changed since \"{latest.Title}\"?"];
    }

    private static bool IsSourceVisible(Source source, Guid userId, CampaignRole role) =>
        role switch
        {
            CampaignRole.GM => true,
            CampaignRole.Player => source.Visibility == VisibilityScope.PartyVisible
                                   || (source.Visibility == VisibilityScope.Private && source.CreatedByUserId == userId),
            _ => source.Visibility == VisibilityScope.PartyVisible
        };

    private static IReadOnlyList<VisibilityScope> GetAllowedScopes(CampaignRole role) =>
        role switch
        {
            CampaignRole.GM => [VisibilityScope.PartyVisible, VisibilityScope.GMOnly, VisibilityScope.Private],
            CampaignRole.Player => [VisibilityScope.PartyVisible, VisibilityScope.Private],
            CampaignRole.Observer => [VisibilityScope.PartyVisible],
            _ => [VisibilityScope.PartyVisible]
        };

    /// <summary>
    /// Stable FNV-1a hash of the campaign id and the current UTC date, so suggestions
    /// rotate daily but stay fixed within a day (string.GetHashCode is randomized per
    /// process and would flicker across server restarts).
    /// </summary>
    internal static uint DaySeed(Guid campaignId, DateOnly? today = null)
    {
        var date = today ?? DateOnly.FromDateTime(DateTime.UtcNow);

        const uint fnvPrime = 16777619;
        var hash = 2166136261;

        foreach (var b in campaignId.ToByteArray())
        {
            hash ^= b;
            hash *= fnvPrime;
        }

        hash ^= (uint)date.DayNumber;
        hash *= fnvPrime;

        return hash;
    }
}
