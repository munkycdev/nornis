using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;

namespace Nornis.Application.Services;

public interface IDemoWorldService
{
    /// <summary>
    /// Instantiates a demo world for the acting user from the template package: a snapshot
    /// copy with fresh ids, the user as sole GM, and an AI-generated name.
    /// </summary>
    Task<AppResult<World>> CreateAsync(CreateDemoWorldCommand command, CancellationToken ct);
}
