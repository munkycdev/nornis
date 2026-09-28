using Nornis.Application.Errors;
using Nornis.Application.Models;

namespace Nornis.Application.Services;

public interface ILoremasterService
{
    /// <summary>
    /// Validates, budgets and grounds the question, then hands back the answer as a stream.
    /// A failure here means nothing was spent on the model; one after it arrives in the stream.
    /// </summary>
    Task<AppResult<LoremasterAnswerStream>> BeginAskAsync(
        AskLoremasterCommand command,
        CancellationToken ct);
}
