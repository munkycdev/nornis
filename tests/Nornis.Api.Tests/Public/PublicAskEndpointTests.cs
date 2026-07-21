using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Api.Tests.Integration;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Public;

/// <summary>
/// Anonymous "Ask the Loremaster" on the public site. It is gated by the GM's monthly public
/// spend cap (which is also the on/off switch), metered as null-user AskLoremaster spend, and
/// resolves through the public slug like every other public read. The AI itself is faked.
/// </summary>
[TestFixture]
[Category("Feature: public-ask")]
public class PublicAskEndpointTests
{
    private const string Slug = "black-harbor";
    private const string GmSub = "auth0|gm-public-ask";

    private LoremasterAskTestFactory _factory = null!;
    private Guid _worldId;
    private HttpClient _gm = null!;
    private HttpClient _anonymous = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new LoremasterAskTestFactory();
        var gmUserId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(
            _factory, GmSub, "gm@example.com", "GM");
        var world = await SourceTestHelpers.CreateTestWorldAsync(_factory, gmUserId);
        _worldId = world.Id;
        _gm = _factory.CreateAuthenticatedClient(sub: GmSub, email: "gm@example.com", nickname: "GM");
        _anonymous = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _gm.Dispose();
        _anonymous.Dispose();
        _factory.Dispose();
    }

    private async Task PublishAsync(decimal? monthlyBudget, bool enabled = true)
    {
        var update = await _gm.PutAsJsonAsync($"/api/worlds/{_worldId}",
            new UpdateWorldRequest(
                PublicSlug: Slug,
                PublicAccessEnabled: enabled,
                PublicAskMonthlyBudgetUsd: monthlyBudget,
                ClearPublicAskBudget: monthlyBudget is null));
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), await update.Content.ReadAsStringAsync());
    }

    private void SeedPublicAskSpend(decimal costUsd)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        db.AiUsageRecords.Add(new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            UserId = null,
            OperationType = AiOperationType.AskLoremaster,
            Model = "gpt-4o",
            EstimatedCostUsd = costUsd,
            Succeeded = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.SaveChanges();
    }

    private Task<HttpResponseMessage> AskAsync(string question) =>
        _anonymous.PostAsJsonAsync($"/api/public/worlds/{Slug}/ask", new PublicAskRequest(question));

    [Test]
    public async Task Ask_EnabledWithinBudget_ReturnsAnswer()
    {
        _factory.FakeAiClient.SetupSuccess("The watch captain is Aldric Vane.");
        await PublishAsync(monthlyBudget: 10m);

        var response = await AskAsync("Who is the watch captain?");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var answer = await response.Content.ReadFromJsonAsync<AskAnswerResponse>();
        Assert.That(answer!.Answer, Does.Contain("Aldric Vane"));
        Assert.That(_factory.FakeAiClient.CallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task Ask_RecordsSpendWithNoUser()
    {
        _factory.FakeAiClient.SetupSuccess("An answer.");
        await PublishAsync(monthlyBudget: 10m);

        await AskAsync("A question?");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var records = db.AiUsageRecords
            .Where(r => r.WorldId == _worldId && r.OperationType == AiOperationType.AskLoremaster)
            .ToList();
        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(records[0].UserId, Is.Null, "public ask spend is attributed to no user — that is how it is metered");
    }

    [Test]
    public async Task Ask_NoBudgetSet_Returns404_AndDoesNotCallAi()
    {
        _factory.FakeAiClient.SetupSuccess("should never be reached");
        await PublishAsync(monthlyBudget: null); // public access on, but Ask left off

        var response = await AskAsync("Who is the watch captain?");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.That(error!.Code, Is.EqualTo("public_ask_unavailable"));
        Assert.That(_factory.FakeAiClient.CallCount, Is.EqualTo(0), "the model is never called when public Ask is disabled");
    }

    [Test]
    public async Task Ask_MonthlyCapReached_Returns429_AndDoesNotCallAi()
    {
        _factory.FakeAiClient.SetupSuccess("should never be reached");
        await PublishAsync(monthlyBudget: 0.50m);
        SeedPublicAskSpend(1.00m); // already over this month's $0.50 cap

        var response = await AskAsync("Who is the watch captain?");

        Assert.That(response.StatusCode, Is.EqualTo((HttpStatusCode)429));
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.That(error!.Code, Is.EqualTo("public_ask_budget_exceeded"));
        Assert.That(_factory.FakeAiClient.CallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task Ask_UnknownSlug_Returns404()
    {
        var response = await _anonymous.PostAsJsonAsync(
            "/api/public/worlds/no-such-world/ask", new PublicAskRequest("Hello?"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Ask_PublicAccessDisabled_Returns404()
    {
        await PublishAsync(monthlyBudget: 10m, enabled: false);

        var response = await AskAsync("Who is the watch captain?");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetWorld_ExposesAskEnabled_WhenBudgetSet()
    {
        await PublishAsync(monthlyBudget: 10m);

        var response = await _anonymous.GetAsync($"/api/public/worlds/{Slug}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var card = await response.Content.ReadFromJsonAsync<PublicWorldResponse>();
        Assert.That(card!.AskEnabled, Is.True);
    }
}
