using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Components.Shared;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// Review's Edit (3.32): it opens on a form, not on the proposal's JSON; what it saves is the
/// payload with only the edited fields changed; and the JSON is one click away for the rest.
/// What the form reads and writes is <c>ProposalEditFormTests</c>' job.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class EditProposalDialogTests : BunitContext
{
    private const string CreateVoss =
        """{"name":"Captain Voss","type":"Character","summary":null,"visibility":"GMOnly","confidence":0.8}""";

    private StubHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new StubHandler();
        var api = new NornisApiClient(
            new HttpClient(_handler) { BaseAddress = new Uri("http://localhost") }, new ViewAsState(), new ActivitySignal(),
            new AuthSessionState());

        Services.AddMudServices();
        Services.AddSingleton(api);
        JSInterop.Mode = JSRuntimeMode.Loose;
        Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    [TearDown]
    public async Task TearDown() => await DisposeAsync();

    private async Task<IRenderedComponent<MudDialogProvider>> OpenAsync(string? changeType, string json)
    {
        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        var parameters = new DialogParameters
        {
            { nameof(EditProposalDialog.WorldId), Guid.NewGuid() },
            { nameof(EditProposalDialog.ProposalId), Guid.NewGuid() },
            { nameof(EditProposalDialog.ProposedValueJson), json },
            { nameof(EditProposalDialog.ChangeType), changeType },
        };

        await dialogs.InvokeAsync(() => Services.GetRequiredService<IDialogService>()
            .ShowAsync<EditProposalDialog>("Edit proposal", parameters));
        dialogs.WaitForAssertion(() => Assert.That(dialogs.Markup, Does.Contain("Save changes")));
        return dialogs;
    }

    private static Task ClickAsync(IRenderedComponent<MudDialogProvider> dialogs, string text) =>
        dialogs.InvokeAsync(() => dialogs.FindAll("button").First(b => b.TextContent.Trim() == text).Click());

    [Test]
    public async Task Edit_OpensOnAForm_NotOnTheJson()
    {
        var dialogs = await OpenAsync("CreateArtifact", CreateVoss);

        Assert.Multiple(() =>
        {
            Assert.That(dialogs.FindAll(".nornis-proposal-json"), Is.Empty);
            Assert.That(dialogs.FindComponents<MudTextField<string>>().Select(f => f.Instance.Label),
                Is.EqualTo(["Name", "Summary"]));
            Assert.That(dialogs.FindComponents<MudSelect<string>>().Select(f => f.Instance.Label),
                Is.EqualTo(["Type", "Who can see it"]));
            Assert.That(dialogs.Markup, Does.Not.Contain("valid JSON"));
        });
    }

    [Test]
    public async Task Saving_SendsThePayload_WithOnlyTheEditedFieldChanged()
    {
        var dialogs = await OpenAsync("CreateArtifact", CreateVoss);
        var name = dialogs.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Name");

        await dialogs.InvokeAsync(() => name.Instance.ValueChanged.InvokeAsync("Captain Mara Voss"));
        await ClickAsync(dialogs, "Save changes");

        dialogs.WaitForAssertion(() => Assert.That(_handler.LastEdit, Is.Not.Null));
        var saved = JsonDocument.Parse(_handler.LastEdit!).RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(saved.GetProperty("name").GetString(), Is.EqualTo("Captain Mara Voss"));
            Assert.That(saved.GetProperty("type").GetString(), Is.EqualTo("Character"));
            Assert.That(saved.GetProperty("visibility").GetString(), Is.EqualTo("GMOnly"));
            Assert.That(saved.GetProperty("confidence").GetDecimal(), Is.EqualTo(0.8m), "what the form does not show is carried through");
            Assert.That(saved.GetProperty("summary").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [Test]
    public async Task AnEmptyRequiredField_IsRefusedInTheDialog_AndNothingIsSent()
    {
        var dialogs = await OpenAsync("CreateArtifact", CreateVoss);
        var name = dialogs.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Name");

        await dialogs.InvokeAsync(() => name.Instance.ValueChanged.InvokeAsync("  "));
        await ClickAsync(dialogs, "Save changes");

        Assert.Multiple(() =>
        {
            Assert.That(dialogs.Find(".mud-alert").TextContent, Does.Contain("Name can't be empty."));
            Assert.That(_handler.LastEdit, Is.Null);
        });
    }

    [Test]
    public async Task Advanced_ShowsTheJson_CarryingWhatTheFormHeld_AndComesBack()
    {
        var dialogs = await OpenAsync("CreateArtifact", CreateVoss);
        var name = dialogs.FindComponents<MudTextField<string>>().Single(f => f.Instance.Label == "Name");
        await dialogs.InvokeAsync(() => name.Instance.ValueChanged.InvokeAsync("Captain Mara Voss"));

        await ClickAsync(dialogs, "Advanced: edit as JSON");

        var json = dialogs.Find(".nornis-proposal-json textarea");
        Assert.That(json.GetAttribute("value") ?? json.TextContent, Does.Contain("\"name\": \"Captain Mara Voss\""),
            "looking at the JSON loses nothing typed");

        await ClickAsync(dialogs, "Back to the form");

        Assert.That(dialogs.FindAll(".nornis-proposal-field input")[0].GetAttribute("value"), Is.EqualTo("Captain Mara Voss"));
    }

    [TestCase("SomethingNew", CreateVoss)]
    [TestCase(null, CreateVoss)]
    [TestCase("CreateArtifact", "[1,2]")]
    public async Task WhatTheFormCannotShow_OpensOnTheJson_WithNoWayBack(string? changeType, string json)
    {
        var dialogs = await OpenAsync(changeType, json);

        Assert.Multiple(() =>
        {
            Assert.That(dialogs.FindAll(".nornis-proposal-json"), Has.Count.EqualTo(1));
            Assert.That(dialogs.FindAll(".nornis-proposal-advanced"), Is.Empty);
        });
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        /// <summary>The proposedValueJson of the last edit that was posted.</summary>
        public string? LastEdit { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.EndsWith("/edit", StringComparison.Ordinal))
            {
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement;
                LastEdit = body.GetProperty("proposedValueJson").GetString();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { proposalId = Guid.NewGuid(), status = "Edited", proposedValueJson = LastEdit }),
                        Encoding.UTF8, "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
