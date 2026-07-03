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

    public async Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync(cancellationToken);
        return user;
    }
}
