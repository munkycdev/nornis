using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryWorldRepository : IWorldRepository
{
    private readonly List<World> _worlds = [];
    private readonly InMemoryWorldMemberRepository? _memberRepository;

    public IReadOnlyList<World> Worlds => _worlds.AsReadOnly();

    public InMemoryWorldRepository()
    {
    }

    /// <summary>
    /// Creates an InMemoryWorldRepository that uses membership records to filter ListByUserAsync,
    /// matching the real EF Core repository behavior.
    /// </summary>
    public InMemoryWorldRepository(InMemoryWorldMemberRepository memberRepository)
    {
        _memberRepository = memberRepository;
    }

    public Task<World> CreateAsync(World world, CancellationToken cancellationToken = default)
    {
        _worlds.Add(world);
        return Task.FromResult(world);
    }

    public Task<World?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var world = _worlds.FirstOrDefault(c => c.Id == id);
        return Task.FromResult(world);
    }

    public Task<World?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        Task.FromResult(_worlds.FirstOrDefault(w =>
            string.Equals(w.PublicSlug, slug, StringComparison.OrdinalIgnoreCase)));

    public Task<World> UpdateAsync(World world, CancellationToken cancellationToken = default)
    {
        var index = _worlds.FindIndex(c => c.Id == world.Id);
        if (index >= 0)
        {
            _worlds[index] = world;
        }
        return Task.FromResult(world);
    }

    public Task<IReadOnlyList<World>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        IEnumerable<World> worlds;

        if (_memberRepository is not null)
        {
            // Match real repository behavior: filter by membership
            var memberWorldIds = _memberRepository.Members
                .Where(m => m.UserId == userId)
                .Select(m => m.WorldId)
                .ToHashSet();
            worlds = _worlds.Where(c => memberWorldIds.Contains(c.Id));
        }
        else
        {
            // Fallback for backward compatibility: filter by CreatedByUserId
            worlds = _worlds.Where(c => c.CreatedByUserId == userId);
        }

        return Task.FromResult<IReadOnlyList<World>>(worlds.ToList().AsReadOnly());
    }

    public Task<IReadOnlyList<World>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
    {
        var worlds = _worlds.Where(c => ids.Contains(c.Id)).ToList();
        return Task.FromResult<IReadOnlyList<World>>(worlds.AsReadOnly());
    }
}
