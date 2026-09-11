using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Application.Services;

/// <summary>
/// The quick switcher's one query, composed from the five services that already know what a
/// reader may see. Campaigns carry no visibility; characters arrive as <c>CharacterView</c>s
/// with hidden links already withheld; artifacts through the artifact search; sessions through
/// the source list projection with its SQL-applied visibility rule; library documents through
/// the library's own listing. Nothing here re-decides any of that, which is why a GM-only
/// artifact that a player cannot see is not merely absent from their results — the response
/// is identical to one in a world where it never existed. The test for that compares the whole
/// response, not a field.
///
/// Matching is a case-insensitive substring over the name, on purpose and only that: the
/// switcher is for people who know what they are looking for. Ranking within a kind is what
/// the owning service returns, except that names that <em>start</em> with the term sort first.
/// </summary>
public sealed class JumpService : IJumpService
{
    private readonly ICampaignService _campaigns;
    private readonly ICharacterService _characters;
    private readonly IArtifactService _artifacts;
    private readonly ISourceService _sources;
    private readonly ILibraryService _library;

    public JumpService(
        ICampaignService campaigns,
        ICharacterService characters,
        IArtifactService artifacts,
        ISourceService sources,
        ILibraryService library)
    {
        _campaigns = campaigns;
        _characters = characters;
        _artifacts = artifacts;
        _sources = sources;
        _library = library;
    }

    public async Task<AppResult<JumpResult>> JumpAsync(JumpQuery query, CancellationToken ct)
    {
        var term = query.Term?.Trim() ?? string.Empty;
        if (term.Length == 0)
        {
            // Blank is a no-op, not an error: the switcher shows the reader's own recents
            // instead, which it keeps itself.
            return AppResult<JumpResult>.Success(new JumpResult([]));
        }

        var perKind = Math.Clamp(query.PerKind, 1, 50);
        var groups = new List<JumpGroup>(5);

        var campaigns = await _campaigns.ListByWorldAsync(query.WorldId, ct);
        if (!campaigns.IsSuccess)
        {
            return AppResult<JumpResult>.Fail(campaigns.Error!);
        }
        groups.Add(Group(JumpKind.Campaign, perKind, term,
            campaigns.Value!.Select(c => (c.Name, new JumpItem(c.Id, c.Name, c.Status.ToString(), c.Slug)))));

        var characters = await _characters.ListByWorldAsync(query.WorldId, query.ActingUserId, query.ActingUserRole, ct);
        if (!characters.IsSuccess)
        {
            return AppResult<JumpResult>.Fail(characters.Error!);
        }
        groups.Add(Group(JumpKind.Character, perKind, term,
            characters.Value!.Select(c => (c.Name, new JumpItem(c.Id, c.Name, "Character", c.Slug)))));

        // Artifacts have a real search with its own ranking and cap. The cap is asked for as
        // one more than the page so "more than this" is knowable without a count query; the
        // total shown is then a floor, which the UI words as such.
        var artifacts = await _artifacts.SearchAsync(
            new ArtifactSearchQuery(query.WorldId, query.ActingUserId, query.ActingUserRole, term, perKind + 1), ct);
        if (!artifacts.IsSuccess)
        {
            return AppResult<JumpResult>.Fail(artifacts.Error!);
        }
        var artifactItems = artifacts.Value!.Select(a => new JumpItem(a.Id, a.Name, a.Type.ToString(), a.Slug)).ToList();
        groups.Add(new JumpGroup(JumpKind.Artifact, artifactItems.Take(perKind).ToList(), artifactItems.Count));

        var sources = await _sources.ListSummariesByWorldAsync(query.WorldId, query.ActingUserId, query.ActingUserRole, ct);
        if (!sources.IsSuccess)
        {
            return AppResult<JumpResult>.Fail(sources.Error!);
        }
        groups.Add(Group(JumpKind.Session, perKind, term,
            sources.Value!.Select(s => (s.Title, new JumpItem(s.Id, s.Title, SessionDetail(s), s.Slug)))));

        var library = await _library.ListAsync(query.WorldId, query.ActingUserRole, ct);
        if (!library.IsSuccess)
        {
            return AppResult<JumpResult>.Fail(library.Error!);
        }
        groups.Add(Group(JumpKind.Library, perKind, term,
            library.Value!.Select(d => (d.Title, new JumpItem(d.Id, d.Title, d.Kind.ToString(), d.Slug)))));

        return AppResult<JumpResult>.Success(new JumpResult(groups.Where(g => g.TotalCount > 0).ToList()));
    }

    private static JumpGroup Group(JumpKind kind, int perKind, string term, IEnumerable<(string Name, JumpItem Item)> candidates)
    {
        var matches = candidates
            .Where(c => c.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => c.Item)
            .ToList();

        return new JumpGroup(kind, matches.Take(perKind).ToList(), matches.Count);
    }

    private static string SessionDetail(SourceListItem s)
    {
        var when = (s.OccurredAt ?? s.CreatedAt).ToString("d MMM yyyy");
        return s.CampaignName is null ? when : $"{when} · {s.CampaignName}";
    }
}
