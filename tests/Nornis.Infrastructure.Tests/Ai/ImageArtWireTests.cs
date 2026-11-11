using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Ai;
using Nornis.Application.Art;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;
using OpenAI;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// What the image generator puts on the wire (features 38, 45): a draft is a generation on the
/// Pictures model, a refine an edit on the refining model that carries the draft itself. The cloud
/// has no OpenAI key, so this is the check that a refine is the request OpenAI's edit endpoint takes.
/// </summary>
[TestFixture]
public class ImageArtWireTests
{
    private static readonly byte[] Draft = Encoding.ASCII.GetBytes("draft-png-bytes");

    [Test]
    public async Task ADraft_IsAGeneration_AtMediumQuality_OnThePicturesTask()
    {
        var (generator, binder, handler) = Build();

        var result = await generator.GenerateAsync(
            AiCallScope.ForWorld(Guid.NewGuid(), Guid.NewGuid()), "a coast", ImageShape.Landscape, 2, 30, "KeyArt", default);

        Assert.Multiple(() =>
        {
            Assert.That(binder.Tasks, Is.EqualTo([AiTask.CoverArt]));
            Assert.That(handler.Path, Does.EndWith("/images/generations"));
            Assert.That(handler.Body, Does.Contain("\"size\":\"1536x1024\""));
            Assert.That(handler.Body, Does.Contain("\"quality\":\"medium\""));
            Assert.That(handler.Body, Does.Contain("\"n\":2"));
            Assert.That(result.Images, Has.Count.EqualTo(1));
            Assert.That((result.Usage.InputTokens, result.Usage.OutputTokens), Is.EqualTo((40, 4000)));
        });
    }

    [Test]
    public async Task ARefine_IsAnEdit_CarryingTheDraft_AtHighQuality_OnTheRefiningTask()
    {
        var (generator, binder, handler) = Build();

        var result = await generator.RefineAsync(
            AiCallScope.ForWorld(Guid.NewGuid(), Guid.NewGuid()), Draft, "Refine this picture.", ImageShape.Portrait, 30,
            "CoverArtRefinement", default);

        Assert.Multiple(() =>
        {
            Assert.That(binder.Tasks, Is.EqualTo([AiTask.ArtRefinement]));
            Assert.That(handler.Path, Does.EndWith("/images/edits"));
            Assert.That(handler.ContentType, Does.StartWith("multipart/form-data"));
            Assert.That(handler.Body, Does.Contain("draft-png-bytes"), "the draft itself is sent");
            Assert.That(handler.Body, Does.Contain("Refine this picture."));
            Assert.That(handler.Body, Does.Contain("1024x1536"));
            Assert.That(handler.Body, Does.Contain("high"));
            Assert.That(handler.Body, Does.Contain("gpt-image-2.6-sunburst"));
            Assert.That(handler.Body, Does.Not.Contain("input_fidelity"), "not documented for the 2.6 models");
            Assert.That(result.Images.Single().Png, Is.EqualTo(Encoding.ASCII.GetBytes("refined")));
            Assert.That(result.Usage.Funding!.Model, Is.EqualTo("gpt-image-2.6-sunburst"));
            Assert.That(binder.Reports, Is.EqualTo([(Exception?)null]));
        });
    }

    [Test]
    public async Task ARefineThatOpenAiRejects_IsARejection_InOpenAisWords()
    {
        var (generator, binder, _) = Build(() => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"error":{"message":"Invalid image.","type":"invalid_request_error","code":"invalid_image"}}""",
                Encoding.UTF8, "application/json"),
        });

        var ex = await Assert.ThrowsAsync<ImageArtRejectedException>(() => generator.RefineAsync(
            AiCallScope.ForWorld(Guid.NewGuid(), Guid.NewGuid()), Draft, "Refine.", ImageShape.Portrait, 30, "CoverArtRefinement", default));

        Assert.That(ex!.Reason, Is.EqualTo("Invalid image."));
        Assert.That(binder.Reports.Single(), Is.SameAs(ex));
    }

    private static (OpenAiImageArtGenerator Generator, FakeImageBinder Binder, CapturingHandler Handler) Build(
        Func<HttpResponseMessage>? respond = null)
    {
        var handler = new CapturingHandler(respond ?? ImagesResponse);
        var binder = new FakeImageBinder(handler);
        return (new OpenAiImageArtGenerator(binder, new NoFunding(), NullLogger<OpenAiImageArtGenerator>.Instance), binder, handler);
    }

    private static HttpResponseMessage ImagesResponse()
    {
        var png = Convert.ToBase64String(Encoding.ASCII.GetBytes("refined"));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"created":1790624127,"data":[{"b64_json":""" + "\"" + png + "\"" + """}],"usage":{"input_tokens":40,"output_tokens":4000,"total_tokens":4040,"input_tokens_details":{"text_tokens":40,"image_tokens":0}}}""",
                Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>Binds every call to an OpenAI image client on <paramref name="handler"/>, naming the
    /// model the funding rule would pick for the task, and records what it was asked.</summary>
    private sealed class FakeImageBinder(HttpMessageHandler handler) : IAiImageBinder
    {
        public List<AiTask> Tasks { get; } = [];

        public List<Exception?> Reports { get; } = [];

        public Task<AiImageBinding> BindImagesAsync(AiCallScope scope, AiTask task, CancellationToken ct)
        {
            Tasks.Add(task);
            var model = task == AiTask.ArtRefinement ? "gpt-image-2.6-sunburst" : "gpt-image-2.6-flare";
            var client = new OpenAIClient(new ApiKeyCredential("test"), new OpenAIClientOptions
            {
                Endpoint = new Uri("https://api.openai.test/v1/"),
                Transport = new HttpClientPipelineTransport(new HttpClient(handler)),
                RetryPolicy = new ClientRetryPolicy(0),
            }).GetImageClient(model);
            var funding = new AiFunding(AiProvider.OpenAi, model, AiFundedBy.Key, Guid.NewGuid(), Guid.NewGuid());
            return Task.FromResult(new AiImageBinding(client, funding, null));
        }

        public Task ReportAsync(AiImageBinding binding, Exception? failure, CancellationToken ct)
        {
            Reports.Add(failure);
            return Task.CompletedTask;
        }
    }

    private sealed class NoFunding : IAiFundingService
    {
        public Task<AiFunding> ResolveAsync(AiCallScope scope, AiTask task, string? nornisModel, bool needsImages, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? Path { get; private set; }

        public string? ContentType { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Path = request.RequestUri?.AbsolutePath;
            ContentType = request.Content?.Headers.ContentType?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return respond();
        }
    }
}
