using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

/// <summary>Hands back a canned <see cref="WorldExportData"/> and records the categories
/// it was asked for.</summary>
public class FakeWorldExportReader : IWorldExportReader
{
    public WorldExportData Data { get; set; } = new();

    public IReadOnlySet<WorldExportCategory>? LastCategories { get; private set; }

    public Task<WorldExportData> ReadAsync(
        Guid worldId,
        IReadOnlySet<WorldExportCategory> categories,
        CancellationToken cancellationToken = default)
    {
        LastCategories = categories;
        return Task.FromResult(Data);
    }
}
