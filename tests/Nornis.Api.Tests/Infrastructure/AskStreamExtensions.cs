using System.Net.ServerSentEvents;
using System.Text.Json;
using Nornis.Api.Contracts.Responses;
using NUnit.Framework;

namespace Nornis.Api.Tests.Infrastructure;

/// <summary>
/// One Ask answer as the server-sent events it arrived as. A successful ask is
/// <c>start</c>, some <c>delta</c>s, then exactly one <c>done</c> or <c>error</c>.
/// </summary>
public sealed class AskStream
{
    public required IReadOnlyList<SseItem<string>> Events { get; init; }

    /// <summary>Reads a whole event stream body.</summary>
    public static async Task<AskStream> ParseAsync(Stream body)
    {
        var events = new List<SseItem<string>>();
        await foreach (var item in SseParser.Create(body).EnumerateAsync())
        {
            events.Add(item);
        }

        return new AskStream { Events = events };
    }

    public IEnumerable<string> EventTypes => Events.Select(e => e.EventType);

    public AskStreamStartResponse? Start => Payload<AskStreamStartResponse>(AskStreamEventNames.Start);

    public IReadOnlyList<string> Deltas => Events
        .Where(e => e.EventType == AskStreamEventNames.Delta)
        .Select(e => Deserialize<AskStreamDeltaResponse>(e.Data).Text)
        .ToList();

    public AskAnswerResponse? Done => Payload<AskAnswerResponse>(AskStreamEventNames.Done);

    public ErrorResponse? Error => Payload<ErrorResponse>(AskStreamEventNames.Error);

    private T? Payload<T>(string eventType) where T : class
    {
        var matching = Events.Where(e => e.EventType == eventType).ToList();
        return matching.Count == 0 ? null : Deserialize<T>(matching.Single().Data);
    }

    // The API writes each payload with the web defaults (camelCase); read them back the same way.
    private static T Deserialize<T>(string data) =>
        JsonSerializer.Deserialize<T>(data, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException($"event payload was null: {data}");
}

public static class AskStreamExtensions
{
    public const string EventStreamMediaType = "text/event-stream";

    /// <summary>Parses a response body as the Ask event stream.</summary>
    public static async Task<AskStream> ReadAskStreamAsync(this HttpContent content)
    {
        Assert.That(content.Headers.ContentType?.MediaType, Is.EqualTo(EventStreamMediaType),
            "a successful ask answers as a server-sent event stream");

        await using var body = await content.ReadAsStreamAsync();
        return await AskStream.ParseAsync(body);
    }

    /// <summary>
    /// The finished answer from a successful ask's stream, the same shape the endpoint used to
    /// return as a JSON body. Fails the test if the stream did not end in <c>done</c>.
    /// </summary>
    public static async Task<AskAnswerResponse?> ReadAskAnswerAsync(this HttpContent content)
    {
        var stream = await content.ReadAskStreamAsync();
        Assert.That(stream.EventTypes.Last(), Is.EqualTo(AskStreamEventNames.Done),
            $"expected the answer stream to finish with done; got {string.Join(", ", stream.EventTypes)}");
        return stream.Done;
    }
}
