namespace Nornis.Domain.Repositories;

public interface IUnitOfWork
{
    Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

public interface ITransactionScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);

    Task RollbackAsync(CancellationToken cancellationToken = default);
}
