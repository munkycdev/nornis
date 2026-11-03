using Nornis.Application.Prep;
using Nornis.Domain.Entities;

namespace Nornis.Application.Tests.Fakes;

/// <summary>
/// An <see cref="AdventurePrintDetails"/> over in-memory repositories holding the world and the
/// creator, for fixtures whose Adventures are viewed. Pass the fixture's own world repository
/// when it already holds the world; otherwise one is made with the world in it.
/// </summary>
public static class TestPrintDetails
{
    public const string Username = "kelda";

    /// <summary>For fixtures that never view or print an Adventure: it knows no world and no user.</summary>
    public static AdventurePrintDetails None => new(new InMemoryWorldRepository(), new InMemoryUserRepository());

    public static AdventurePrintDetails For(
        Guid worldId, Guid creatorId, string? gameSystem = null, InMemoryWorldRepository? worlds = null)
    {
        var now = DateTimeOffset.UtcNow;
        if (worlds is null)
        {
            worlds = new InMemoryWorldRepository();
            worlds.CreateAsync(new World
            {
                Id = worldId,
                Name = "Black Harbor",
                GameSystem = gameSystem,
                CreatedByUserId = creatorId,
                CreatedAt = now,
                UpdatedAt = now,
            }).GetAwaiter().GetResult();
        }

        var users = new InMemoryUserRepository();
        users.CreateAsync(new User
        {
            Id = creatorId,
            Username = Username,
            Email = "kelda@blackharbor.com",
            CreatedAt = now,
            UpdatedAt = now,
        }, $"auth0|{creatorId:N}").GetAwaiter().GetResult();

        return new AdventurePrintDetails(worlds, users);
    }
}
