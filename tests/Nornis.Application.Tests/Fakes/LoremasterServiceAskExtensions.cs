using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;

namespace Nornis.Application.Tests.Fakes;

/// <summary>
/// Reads a Loremaster answer the way a caller that wants the whole thing would: begin the ask,
/// drain the stream, and report how it ended. Most service tests assert on the finished answer
/// or on the error, not on the deltas, and this keeps their assertions meaning what they said
/// before answers streamed: a refusal before the stream and a failure inside it both arrive
/// as a failed result.
/// </summary>
public static class LoremasterServiceAskExtensions
{
    public static async Task<AppResult<LoremasterAnswer>> AskAsync(
        this ILoremasterService service,
        AskLoremasterCommand command,
        CancellationToken ct)
    {
        var begun = await service.BeginAskAsync(command, ct);
        if (!begun.IsSuccess)
        {
            return AppResult<LoremasterAnswer>.Fail(begun.Error!);
        }

        AppResult<LoremasterAnswer>? outcome = null;
        await foreach (var e in begun.Value!.Events)
        {
            switch (e)
            {
                case LoremasterStreamEvent.Completed completed:
                    outcome = AppResult<LoremasterAnswer>.Success(completed.Answer);
                    break;
                case LoremasterStreamEvent.Failed failed:
                    outcome = AppResult<LoremasterAnswer>.Fail(failed.Error);
                    break;
            }
        }

        return outcome ?? throw new InvalidOperationException(
            "The answer stream ended without a Completed or Failed event.");
    }

    /// <summary>Every event of one answer, in order. Throws on a pre-stream refusal.</summary>
    public static async Task<IReadOnlyList<LoremasterStreamEvent>> CollectEventsAsync(
        this ILoremasterService service,
        AskLoremasterCommand command,
        CancellationToken ct)
    {
        var begun = await service.BeginAskAsync(command, ct);
        if (!begun.IsSuccess)
        {
            throw new InvalidOperationException(
                $"The ask was refused before streaming: {begun.Error!.Code}");
        }

        var events = new List<LoremasterStreamEvent>();
        await foreach (var e in begun.Value!.Events)
        {
            events.Add(e);
        }

        return events;
    }
}
