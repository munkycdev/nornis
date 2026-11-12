using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

/// <summary>
/// The world's standing instructions (feature 44). Always read whole, per world: the extraction
/// prompt renders every line, and the cap keeps "whole" small.
/// </summary>
public interface IEditorialRuleRepository
{
    Task<EditorialRule> CreateAsync(EditorialRule rule, CancellationToken cancellationToken = default);

    Task<EditorialRule?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every rule of the world, oldest first.</summary>
    Task<IReadOnlyList<EditorialRule>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default);

    /// <summary>Removes the rule. A rule that is already gone is gone.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
