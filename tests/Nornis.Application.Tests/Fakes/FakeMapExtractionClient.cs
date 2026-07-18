using Nornis.Application.Ai;

namespace Nornis.Application.Tests.Fakes;

public class FakeMapExtractionClient : IMapExtractionClient
{
    public IReadOnlyList<MapPlace> PlacesToReturn { get; set; } = [];
    public Exception? ExceptionToThrow { get; set; }
    public int CallCount { get; private set; }
    public MapExtractionRequest? LastRequest { get; private set; }

    public Task<MapExtractionResponse> ExtractAsync(MapExtractionRequest request, CancellationToken ct)
    {
        CallCount++;
        LastRequest = request;

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        return Task.FromResult(new MapExtractionResponse
        {
            Places = PlacesToReturn,
            InputTokens = 3000,
            OutputTokens = 500,
            TotalTokens = 3500,
            DurationMs = 1200,
            Model = request.Model
        });
    }
}
