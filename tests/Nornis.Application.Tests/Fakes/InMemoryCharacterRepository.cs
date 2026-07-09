using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryCharacterRepository : ICharacterRepository
{
    private readonly List<Character> _characters = [];
    private readonly List<CampaignCharacter> _assignments = [];

    public IReadOnlyList<Character> Characters => _characters.AsReadOnly();

    public IReadOnlyList<CampaignCharacter> Assignments => _assignments.AsReadOnly();

    public void Seed(params Character[] characters) => _characters.AddRange(characters);

    public void SeedAssignments(params CampaignCharacter[] assignments) => _assignments.AddRange(assignments);

    public Task<Character> CreateAsync(Character character, CancellationToken cancellationToken = default)
    {
        _characters.Add(character);
        return Task.FromResult(character);
    }

    public Task<Character?> GetByIdAsync(Guid characterId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(WithAssignments(_characters.FirstOrDefault(c => c.Id == characterId)));
    }

    public Task<IReadOnlyList<Character>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        var characters = _characters
            .Where(c => c.WorldId == worldId)
            .Select(c => WithAssignments(c)!)
            .OrderBy(c => c.Name)
            .ToList();
        return Task.FromResult<IReadOnlyList<Character>>(characters.AsReadOnly());
    }

    public Task<IReadOnlyList<Character>> ListByCampaignAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        var characterIds = _assignments
            .Where(a => a.CampaignId == campaignId)
            .Select(a => a.CharacterId)
            .ToHashSet();

        var characters = _characters
            .Where(c => characterIds.Contains(c.Id))
            .Select(c => WithAssignments(c)!)
            .OrderBy(c => c.Name)
            .ToList();
        return Task.FromResult<IReadOnlyList<Character>>(characters.AsReadOnly());
    }

    public Task<Character> UpdateAsync(Character character, CancellationToken cancellationToken = default)
    {
        var index = _characters.FindIndex(c => c.Id == character.Id);
        if (index >= 0)
        {
            _characters[index] = character;
        }
        return Task.FromResult(character);
    }

    public Task DeleteAsync(Guid characterId, CancellationToken cancellationToken = default)
    {
        _assignments.RemoveAll(a => a.CharacterId == characterId);
        _characters.RemoveAll(c => c.Id == characterId);
        return Task.CompletedTask;
    }

    public Task ReplaceCampaignAssignmentsAsync(Guid campaignId, IReadOnlyCollection<Guid> characterIds, CancellationToken cancellationToken = default)
    {
        _assignments.RemoveAll(a => a.CampaignId == campaignId && !characterIds.Contains(a.CharacterId));

        var existing = _assignments.Where(a => a.CampaignId == campaignId).Select(a => a.CharacterId).ToHashSet();
        foreach (var characterId in characterIds.Where(id => !existing.Contains(id)))
        {
            _assignments.Add(new CampaignCharacter
            {
                Id = Guid.NewGuid(),
                CampaignId = campaignId,
                CharacterId = characterId,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        return Task.CompletedTask;
    }

    internal void RemoveAssignmentsByCampaign(Guid campaignId) =>
        _assignments.RemoveAll(a => a.CampaignId == campaignId);

    private Character? WithAssignments(Character? character)
    {
        if (character is null)
        {
            return null;
        }

        character.CampaignCharacters = _assignments.Where(a => a.CharacterId == character.Id).ToList();
        return character;
    }
}
