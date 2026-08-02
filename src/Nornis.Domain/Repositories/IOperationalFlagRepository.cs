using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

/// <summary>
/// The operator's switches. Read by every host on a cache; written only by
/// <c>scripts/ai-pause.ps1</c>, because a switch with a UI is a switch someone clicks by
/// accident.
/// </summary>
public interface IOperationalFlagRepository
{
    /// <summary>Null when the flag has never been set, which reads the same as off.</summary>
    Task<OperationalFlag?> GetAsync(string name, CancellationToken cancellationToken = default);
}
