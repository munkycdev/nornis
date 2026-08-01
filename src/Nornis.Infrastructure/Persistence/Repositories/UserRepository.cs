using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class UserRepository : IUserRepository
{
    private readonly NornisDbContext _context;

    public UserRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<User> CreateAsync(User user, CancellationToken cancellationToken = default)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<User?> GetByAuth0SubjectIdAsync(string auth0SubjectId, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Auth0SubjectId == auth0SubjectId, cancellationToken);
    }

    public async Task<IReadOnlyList<User>> ListAddableToWorldAsync(
        Guid worldId, string? search, int limit, CancellationToken cancellationToken = default)
    {
        var query = _context.Users
            .AsNoTracking()
            .Where(u => !_context.WorldMembers.Any(m => m.WorldId == worldId && m.UserId == u.Id));

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Contains, not StartsWith: usernames here are Discord handles, and a GM adding
            // someone usually recalls a fragment rather than the opening characters.
            var term = search.Trim();
            query = query.Where(u => u.Username.Contains(term));
        }

        return await query
            .OrderBy(u => u.Username)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        await _context.SaveAndDetachAsync(user, cancellationToken);
        return user;
    }
}
