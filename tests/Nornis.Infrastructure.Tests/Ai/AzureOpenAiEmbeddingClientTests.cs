using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Infrastructure.Ai;
using NSubstitute;
using NUnit.Framework;
using OpenAI.Embeddings;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// The embedding client's failure translation. Embeddings were the one AI path with no
/// application timeout, so a hung call fell back to the SDK's own worst case — long enough for
/// the worker's library lock to lapse and Service Bus to deliver a second run that re-buys every
/// embedding the first had already paid for.
/// </summary>
[TestFixture]
public class AzureOpenAiEmbeddingClientTests
{
    private EmbeddingClient _sdkClient = null!;

    [SetUp]
    public void SetUp() => _sdkClient = Substitute.For<EmbeddingClient>();

    private AzureOpenAiEmbeddingClient MakeClient(int timeoutSeconds) =>
        new(_sdkClient, Options.Create(new LibraryOptions { AiTimeoutSeconds = timeoutSeconds }));

    /// <summary>A call that honours its token but never returns on its own.</summary>
    private static async Task<ClientResult<OpenAIEmbeddingCollection>> HangAsync(CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct);
        throw new UnreachableException();
    }

    private void SetupHangingCall() =>
        _sdkClient
            .GenerateEmbeddingsAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<EmbeddingGenerationOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(call => HangAsync(call.Arg<CancellationToken>()));

    [Test]
    public void EmbedAsync_WhenTheCallHangs_ThrowsAiTimeout()
    {
        SetupHangingCall();

        var ex = Assert.ThrowsAsync<AiTimeoutException>(
            async () => await MakeClient(timeoutSeconds: 1).EmbedAsync(["chunk"], CancellationToken.None));

        // AiTimeoutException specifically, because TransientFailureClassifier reads it as
        // transient — a slow embedding retries instead of permanently failing the document.
        Assert.That(ex!.Message, Does.Contain("1 seconds"));
    }

    [Test]
    public void EmbedAsync_WhenTheCallerCancels_DoesNotClaimATimeout()
    {
        SetupHangingCall();

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        // The caller leaving is not a service failure, and the classifier reads the two
        // oppositely — a timeout retries, a cancellation does not. Calling this a timeout would
        // make an abandoned request look like something worth re-buying.
        // CatchAsync, not ThrowsAsync: the SDK surfaces the derived TaskCanceledException, and
        // what matters is that it is still an OperationCanceledException rather than a timeout.
        Assert.CatchAsync<OperationCanceledException>(
            async () => await MakeClient(timeoutSeconds: 30).EmbedAsync(["chunk"], cts.Token));
    }

    [Test]
    public void EmbedAsync_WhenTheServiceReturnsAnError_TranslatesTheStatus()
    {
        // Built before the Returns call, not inside it: a substitute created mid-recording
        // becomes the call NSubstitute thinks you are configuring.
        var response = Substitute.For<PipelineResponse>();
        response.Status.Returns(429);
        var failure = new ClientResultException("throttled", response);

        _sdkClient
            .GenerateEmbeddingsAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<EmbeddingGenerationOptions>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<ClientResult<OpenAIEmbeddingCollection>>>(_ => throw failure);

        // Translated at the boundary so the application layer can classify on a typed status
        // instead of matching prose, and so a 429 retries rather than failing the document.
        var ex = Assert.ThrowsAsync<HttpRequestException>(
            async () => await MakeClient(timeoutSeconds: 30).EmbedAsync(["chunk"], CancellationToken.None));

        Assert.That(ex!.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.TooManyRequests));
    }
}
