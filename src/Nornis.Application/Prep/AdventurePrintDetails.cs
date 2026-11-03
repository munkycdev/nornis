using Nornis.Domain.Entities;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Prep;

/// <summary>
/// Finds what <see cref="PrintDetails.Resolve"/> needs beyond the Adventure itself, the world's
/// game system and the creator's username, and resolves the details a print would carry now
/// (feature 32). The Details card's preview, the print page and the Library's print snapshot all
/// come through here, so the three can't disagree about where the author or the fallback game
/// system comes from.
/// </summary>
public class AdventurePrintDetails
{
    private readonly IWorldRepository _worlds;
    private readonly IUserRepository _users;

    public AdventurePrintDetails(IWorldRepository worlds, IUserRepository users)
    {
        _worlds = worlds;
        _users = users;
    }

    public async Task<PrintDetails> ResolveAsync(Adventure adventure, CancellationToken ct)
    {
        // Both are required foreign keys with Restrict deletes, so neither can be missing.
        var world = await _worlds.GetByIdAsync(adventure.WorldId, ct)
            ?? throw new InvalidOperationException($"World '{adventure.WorldId}' of Adventure '{adventure.Id}' not found.");
        var creator = await _users.GetByIdAsync(adventure.CreatedByUserId, ct)
            ?? throw new InvalidOperationException($"Creator '{adventure.CreatedByUserId}' of Adventure '{adventure.Id}' not found.");

        return PrintDetails.Resolve(adventure.Title, adventure.Details, world.GameSystem, creator.Username);
    }
}
