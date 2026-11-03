using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Ai;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// Saving a key (feature 39): what each answer from the provider means, that the key never
/// reaches a log, and that an OpenAI-compatible endpoint can't point Nornis at a private address.
/// </summary>
[TestFixture]
public class AiKeyCheckerTests
{
    private const string Secret = "sk-proj-redaction-canary-1234";

    [TestCase(HttpStatusCode.OK, AiKeyCheckOutcome.Accepted)]
    [TestCase(HttpStatusCode.Unauthorized, AiKeyCheckOutcome.Rejected)]
    [TestCase(HttpStatusCode.Forbidden, AiKeyCheckOutcome.Rejected)]
    [TestCase(HttpStatusCode.NotFound, AiKeyCheckOutcome.NotFound)]
    [TestCase(HttpStatusCode.InternalServerError, AiKeyCheckOutcome.Unreachable)]
    [TestCase(HttpStatusCode.TooManyRequests, AiKeyCheckOutcome.Unreachable)]
    public async Task The_providers_answer_decides_the_outcome(HttpStatusCode status, AiKeyCheckOutcome expected)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(status));
        var checker = new AiKeyChecker(new HttpClient(handler), NullLogger<AiKeyChecker>.Instance);

        var outcome = await checker.CheckAsync(AiProvider.OpenAi, Secret, null, CancellationToken.None);

        Assert.That(outcome, Is.EqualTo(expected));
        Assert.That(handler.Requests.Single().RequestUri, Is.EqualTo(new Uri("https://api.openai.com/v1/models")));
        Assert.That(handler.Requests.Single().Headers.Authorization?.ToString(), Is.EqualTo("Bearer " + Secret));
    }

    [Test]
    public async Task A_compatible_key_is_checked_at_its_own_base_url()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var checker = new AiKeyChecker(new HttpClient(handler), NullLogger<AiKeyChecker>.Instance);

        await checker.CheckAsync(AiProvider.OpenAiCompatible, Secret, new Uri("https://llm.example.com/openai/v1/"), CancellationToken.None);

        Assert.That(handler.Requests.Single().RequestUri, Is.EqualTo(new Uri("https://llm.example.com/openai/v1/models")));
    }

    [Test]
    public async Task A_network_failure_is_unreachable_not_rejected()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("no route"));
        var checker = new AiKeyChecker(new HttpClient(handler), NullLogger<AiKeyChecker>.Instance);

        var outcome = await checker.CheckAsync(AiProvider.OpenAi, Secret, null, CancellationToken.None);

        Assert.That(outcome, Is.EqualTo(AiKeyCheckOutcome.Unreachable));
    }

    /// <summary>
    /// Through the real registration and the HTTP client factory's own logging at its most
    /// verbose, the key appears in no log line. Seen failing first with
    /// <c>.RedactLoggedHeaders(_ =&gt; false)</c> on the registration, which logs the header.
    /// </summary>
    [Test]
    public async Task The_key_appears_in_no_log_line()
    {
        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        services.AddHttpClient<IAiKeyChecker, AiKeyChecker>()
            .ConfigurePrimaryHttpMessageHandler(() => new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IAiKeyChecker>().CheckAsync(AiProvider.OpenAi, Secret, null, CancellationToken.None);

        Assert.That(logs.Lines, Is.Not.Empty, "the factory logged the request, so the assertion below means something");
        Assert.That(logs.Lines, Has.None.Contains(Secret));
        Assert.That(logs.Lines, Has.None.Contains("redaction-canary"));
    }

    /// <summary>The same for a call made with a user's key: the SDK's pipeline logs nothing of it.</summary>
    [Test]
    public async Task A_call_with_a_users_key_logs_no_header_either()
    {
        var handler = new RecordingHandler(_ => Completion());
        using var clients = new UserKeyChatClients(maxRetries: 0, compatibleHandler: handler);
        var key = new UserAiKey
        {
            Id = Guid.NewGuid(),
            Provider = AiProvider.OpenAiCompatible,
            BaseUrl = "https://llm.example.com/v1/",
            UpdatedAt = DateTimeOffset.UnixEpoch,
        };
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllDataAndRecorded,
        };
        var tags = new ConcurrentBag<string>();
        listener.ActivityStopped = a => { foreach (var tag in a.TagObjects) { tags.Add($"{tag.Key}={tag.Value}"); } };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        var response = await clients.For(key, "llama-4", () => Secret).GetResponseAsync("Who sank the Gull?");

        Assert.That(response.Text, Is.EqualTo("Voss"));
        Assert.That(handler.Requests.Single().RequestUri!.Host, Is.EqualTo("llm.example.com"), "through the compatible handler");
        Assert.That(tags, Has.None.Contains(Secret));
    }

    [Test]
    public void The_real_compatible_transport_refuses_a_private_endpoint()
    {
        using var clients = new UserKeyChatClients(maxRetries: 0);
        var key = new UserAiKey
        {
            Id = Guid.NewGuid(),
            Provider = AiProvider.OpenAiCompatible,
            BaseUrl = "https://127.0.0.1:9/v1/",
            UpdatedAt = DateTimeOffset.UnixEpoch,
        };

        var thrown = Assert.CatchAsync(() => clients.For(key, "llama-4", () => Secret).GetResponseAsync("hello"));

        Assert.That(Flatten(thrown).Any(e => e.Message.Contains("not a public internet address")), Is.True, thrown!.ToString());
    }

    [TestCase("127.0.0.1")]
    [TestCase("10.1.2.3")]
    [TestCase("172.16.0.1")]
    [TestCase("172.31.255.255")]
    [TestCase("192.168.1.10")]
    [TestCase("169.254.169.254")]
    [TestCase("100.64.0.1")]
    [TestCase("0.0.0.0")]
    [TestCase("224.0.0.1")]
    [TestCase("::1")]
    [TestCase("fe80::1")]
    [TestCase("fd00::1")]
    [TestCase("::ffff:169.254.169.254")]
    [TestCase("2002:a9fe:a9fe::1")]
    public void Private_and_reserved_addresses_are_not_public(string address) =>
        Assert.That(PublicEndpointGuard.IsPublic(IPAddress.Parse(address)), Is.False);

    [TestCase("8.8.8.8")]
    [TestCase("172.32.0.1")]
    [TestCase("20.42.65.92")]
    [TestCase("2606:4700:4700::1111")]
    public void Public_addresses_are_public(string address) =>
        Assert.That(PublicEndpointGuard.IsPublic(IPAddress.Parse(address)), Is.True);

    private static IEnumerable<Exception> Flatten(Exception? exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            yield return e;
        }
    }

    private static HttpResponseMessage Completion() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"id":"c1","object":"chat.completion","created":1790624127,"model":"llama-4",
                 "choices":[{"index":0,"message":{"role":"assistant","content":"Voss"},"finish_reason":"stop"}],
                 "usage":{"prompt_tokens":5,"completion_tokens":1,"total_tokens":6}}
                """, Encoding.UTF8, "application/json"),
        };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(CapturingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var structured = state is IEnumerable<KeyValuePair<string, object?>> pairs
                    ? string.Join(";", pairs.Select(p => $"{p.Key}={p.Value}"))
                    : string.Empty;
                provider.Lines.Enqueue($"{formatter(state, exception)} | {structured}");
            }
        }
    }
}
