using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class FakeUnitOfWork : IUnitOfWork
{
    private readonly List<FakeTransactionScope> _transactions = [];
    private bool _shouldFailOnCommit;

    public IReadOnlyList<FakeTransactionScope> Transactions => _transactions.AsReadOnly();

    public void ConfigureCommitFailure(bool shouldFail = true)
    {
        _shouldFailOnCommit = shouldFail;
    }

    public Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = new FakeTransactionScope(_shouldFailOnCommit);
        _transactions.Add(transaction);
        return Task.FromResult<ITransactionScope>(transaction);
    }
}

public class FakeTransactionScope : ITransactionScope
{
    private readonly bool _shouldFailOnCommit;

    public bool Committed { get; private set; }
    public bool RolledBack { get; private set; }
    public bool Disposed { get; private set; }

    public FakeTransactionScope(bool shouldFailOnCommit = false)
    {
        _shouldFailOnCommit = shouldFailOnCommit;
    }

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_shouldFailOnCommit)
        {
            throw new InvalidOperationException("Simulated transaction commit failure.");
        }

        Committed = true;
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        RolledBack = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
