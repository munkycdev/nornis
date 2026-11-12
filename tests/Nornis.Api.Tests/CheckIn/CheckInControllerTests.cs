using System.Net;
using System.Net.Http.Json;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using NUnit.Framework;

namespace Nornis.Api.Tests.CheckIn;

/// <summary>
/// The check-in endpoints (feature 44) through the test host: the GM gate, the read, a start on
/// a world with nothing to doubt, the answer's input checks, the standing instructions, and the
/// per-world question count on the world update.
/// </summary>
[TestFixture]
public class CheckInControllerTests
{
    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new NornisWebApplicationFactory();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    [Test]
    [Category("Authorization")]
    public async Task Get_NonGm_ReturnsForbidden()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var player = await scenario.PlayerClient.GetAsync($"/api/worlds/{scenario.World.Id}/check-in");
        var observer = await scenario.ObserverClient.PostAsync($"/api/worlds/{scenario.World.Id}/check-in/start", null);

        Assert.That(player.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(observer.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Get_Gm_EmptyWorld_IsNotDue_AndHasNoRound()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var response = await scenario.GmClient.GetAsync($"/api/worlds/{scenario.World.Id}/check-in");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var view = await response.Content.ReadFromJsonAsync<CheckInResponse>();
        Assert.Multiple(() =>
        {
            Assert.That(view!.Due, Is.False);
            Assert.That(view.Current, Is.Null);
            Assert.That(view.Rules, Is.Empty);
            Assert.That(view.QuestionCount, Is.EqualTo(5));
            Assert.That(view.CadenceSessions, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task Start_Gm_EmptyWorld_OpensARoundWithNoQuestions()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var response = await scenario.GmClient.PostAsync($"/api/worlds/{scenario.World.Id}/check-in/start", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var view = await response.Content.ReadFromJsonAsync<CheckInResponse>();
        Assert.That(view!.Current, Is.Not.Null);
        Assert.That(view.Current!.Questions, Is.Empty);
        Assert.That(view.LastCheckInAt, Is.Not.Null);

        var again = await scenario.GmClient.GetFromJsonAsync<CheckInResponse>($"/api/worlds/{scenario.World.Id}/check-in");
        Assert.That(again!.Current!.Id, Is.EqualTo(view.Current.Id));
    }

    [Test]
    public async Task Answer_UnknownAnswerName_Is400_UnknownQuestion_Is404()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var bad = await scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/check-in/questions/{Guid.NewGuid()}/answer", new { answer = "Maybe" });
        var missing = await scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/check-in/questions/{Guid.NewGuid()}/answer", new { answer = "Skip" });

        Assert.That(bad.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Rules_RoundTrip_AndAreCappedInLength()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var baseUrl = $"/api/worlds/{scenario.World.Id}/check-in";

        var added = await scenario.GmClient.PostAsJsonAsync($"{baseUrl}/rules", new { text = "  Shop inventories are background. " });
        Assert.That(added.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var rule = await added.Content.ReadFromJsonAsync<EditorialRuleResponse>();
        Assert.That(rule!.Text, Is.EqualTo("Shop inventories are background."));
        Assert.That(rule.Origin, Is.EqualTo("Manual"));

        var tooLong = await scenario.GmClient.PostAsJsonAsync($"{baseUrl}/rules", new { text = new string('x', 301) });
        Assert.That(tooLong.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var player = await scenario.PlayerClient.PostAsJsonAsync($"{baseUrl}/rules", new { text = "mine" });
        Assert.That(player.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        var view = await scenario.GmClient.GetFromJsonAsync<CheckInResponse>(baseUrl);
        Assert.That(view!.Rules.Select(r => r.Id), Is.EqualTo([rule.Id]));

        var removed = await scenario.GmClient.DeleteAsync($"{baseUrl}/rules/{rule.Id}");
        Assert.That(removed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        var after = await scenario.GmClient.GetFromJsonAsync<CheckInResponse>(baseUrl);
        Assert.That(after!.Rules, Is.Empty);
    }

    [Test]
    public async Task World_QuestionCount_UpdatesThroughThePartialUpdate_AndIsHeldToTen()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var tooMany = await scenario.GmClient.PutAsJsonAsync($"/api/worlds/{scenario.World.Id}", new { checkInQuestionCount = 11 });
        Assert.That(tooMany.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var seven = await scenario.GmClient.PutAsJsonAsync($"/api/worlds/{scenario.World.Id}", new { checkInQuestionCount = 7 });
        Assert.That(seven.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var world = await seven.Content.ReadFromJsonAsync<WorldResponse>();
        Assert.That(world!.CheckInQuestionCount, Is.EqualTo(7));

        var view = await scenario.GmClient.GetFromJsonAsync<CheckInResponse>($"/api/worlds/{scenario.World.Id}/check-in");
        Assert.That(view!.QuestionCount, Is.EqualTo(7));

        var list = await scenario.GmClient.GetFromJsonAsync<List<WorldListItemResponse>>("/api/worlds");
        Assert.That(list!.Single(w => w.Id == scenario.World.Id).CheckInQuestionCount, Is.EqualTo(7));
    }
}
