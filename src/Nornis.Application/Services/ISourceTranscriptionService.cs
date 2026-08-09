using Nornis.Application.Errors;
using Nornis.Application.Models;

namespace Nornis.Application.Services;

public interface ISourceTranscriptionService
{
    Task<AppResult<SourceTranscriptionResult>> TranscribeAsync(
        TranscribeSourceCommand command, CancellationToken ct);
}
