using System.Net;
using System.Text;

namespace Nornis.Infrastructure.Tests.Bibliotaph;

/// <summary>A stand-in Bibliotaph (or Auth0): answers each request with what the test says, and records it.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _answer;

    public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) => _answer = answer;

    public StubHandler(HttpStatusCode status, string? json = null)
        : this((_, _) => Task.FromResult(Respond(status, json)))
    {
    }

    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

    public static HttpResponseMessage Respond(HttpStatusCode status, string? json = null) =>
        new(status) { Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        return await _answer(request, cancellationToken);
    }
}

internal sealed class ManualTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class SingleClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;

    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}
