using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class CharacterRepository : ICharacterRepository
{
    private readonly NornisDbContext _context;

    public CharacterRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<Character> CreateAsync(Character character, CancellationToken cancellationToken = default)
    {
        _context.Characters.Add(character);
        await _context.SaveChangesAsync(cancellationToken);
        return character;
    }

    public async Task<Character?> GetByIdAsync(Guid characterId, CancellationToken cancellationToken = default)
    {
        return await _context.Characters
            .AsNoTracking()
            .Include(c => c.CampaignCharacters)
            .FirstOrDefaultAsync(c => c.Id == characterId, cancellationToken);
    }

    public async Task<IReadOnlyList<Character>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        return await _context.Characters
            .AsNoTracking()
            .Include(c => c.CampaignCharacters)
            .Where(c => c.WorldId == worldId)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Character>> ListByCampaignAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        return await _context.CampaignCharacters
            .AsNoTracking()
            .Where(cc => cc.CampaignId == campaignId)
            .Select(cc => cc.Character)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Character> UpdateAsync(Character character, CancellationToken cancellationToken = default)
    {
        _context.Characters.Update(character);
        await _context.SaveChangesAsync(cancellationToken);
        return character;
    }

    public async Task DeleteAsync(Guid characterId, CancellationToken cancellationToken = default)
    {
        // Campaign assignments cascade from the character in the database.
        await _context.Characters
            .Where(c => c.Id == characterId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task ReplaceCampaignAssignmentsAsync(Guid campaignId, IReadOnlyCollection<Guid> characterIds, CancellationToken cancellationToken = default)
    {
        var existing = await _context.CampaignCharacters
            .Where(cc => cc.CampaignId == campaignId)
            .ToListAsync(cancellationToken);

        var desired = characterIds.ToHashSet();
        var current = existing.Select(cc => cc.CharacterId).ToHashSet();

        _context.CampaignCharacters.RemoveRange(existing.Where(cc => !desired.Contains(cc.CharacterId)));

        var now = DateTimeOffset.UtcNow;
        foreach (var characterId in desired.Except(current))
        {
            _context.CampaignCharacters.Add(new CampaignCharacter
            {
                Id = Guid.NewGuid(),
                CampaignId = campaignId,
                CharacterId = characterId,
                CreatedAt = now
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
