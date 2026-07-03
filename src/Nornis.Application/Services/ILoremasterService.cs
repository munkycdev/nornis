using Nornis.Application.Errors;
using Nornis.Application.Models;

namespace Nornis.Application.Services;

public interface ILoremasterService
{
    Task<AppResult<LoremasterAnswer>> AskAsync(
        AskLoremasterCommand command,
        CancellationToken ct);
}
