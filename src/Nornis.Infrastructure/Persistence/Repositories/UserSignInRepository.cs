using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class UserSignInRepository : IUserSignInRepository
{
    private readonly NornisDbContext _context;

    public UserSignInRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<UserSignIn>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await _context.UserSignIns
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.AddedAt)
            .ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

    public Task<UserSignIn?> GetBySubjectAsync(string subject, CancellationToken cancellationToken = default) =>
        _context.UserSignIns
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Subject == subject, cancellationToken);

    public Task AddAsync(UserSignIn signIn, CancellationToken cancellationToken = default) =>
        _context.AddAndDetachAsync(signIn, cancellationToken);

    public Task DeleteAsync(Guid signInId, CancellationToken cancellationToken = default) =>
        _context.DeleteWhereAsync<UserSignIn>(s => s.Id == signInId, cancellationToken);

    public async Task SetLinkCodeAsync(
        Guid userId, string codeHash, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        var code = await _context.SignInLinkCodes.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);
        if (code is null)
        {
            code = new SignInLinkCode { UserId = userId };
            _context.SignInLinkCodes.Add(code);
        }

        code.CodeHash = codeHash;
        code.ExpiresAt = expiresAt;
        await _context.SaveChangesAsync(cancellationToken);
        _context.Entry(code).State = EntityState.Detached;
    }

    public async Task<SignInLinkCode?> TakeLinkCodeAsync(string codeHash, CancellationToken cancellationToken = default)
    {
        var code = await _context.SignInLinkCodes.FirstOrDefaultAsync(c => c.CodeHash == codeHash, cancellationToken);
        if (code is null)
        {
            return null;
        }

        _context.SignInLinkCodes.Remove(code);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request spent it first. A code is good once.
            _context.Entry(code).State = EntityState.Detached;
            return null;
        }

        _context.Entry(code).State = EntityState.Detached;
        return code;
    }
}
