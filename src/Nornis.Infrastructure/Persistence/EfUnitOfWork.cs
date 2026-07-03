using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence;

public class EfUnitOfWork : IUnitOfWork
{
    private readonly NornisDbContext _dbContext;

    public EfUnitOfWork(NornisDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        return new EfTransactionScope(transaction);
    }

    private sealed class EfTransactionScope : ITransactionScope
    {
        private readonly IDbContextTransaction _transaction;

        public EfTransactionScope(IDbContextTransaction transaction)
        {
            _transaction = transaction;
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
            => _transaction.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default)
            => _transaction.RollbackAsync(cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await _transaction.DisposeAsync();
        }
    }
}
