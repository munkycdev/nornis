using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Application.Ai;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Authentication;

/// <summary>
/// Profile's AI keys (feature 39), end to end: a key never comes back out, a check that fails
/// saves nothing, nobody reaches another user's key, saves are rate-limited, and saving a key
/// queues exactly the sources that failed for want of one in the worlds the user created.
/// </summary>
[TestFixture]
[Category("Authorization")]
public class AiKeysEndpointTests
{
    private const string Secret = "sk-proj-voss-secret-7Q2x";
    private const string VossSub = "auth0|voss-ai-keys";
    private const string MireSub = "auth0|mire-ai-keys";

    private NornisWebApplicationFactory _factory = null!;
    private HttpClient _voss = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new NornisWebApplicationFactory();
        _voss = _factory.CreateAuthenticatedClient(sub: VossSub, email: "voss@blackharbor.test");
    }

    [TearDown]
    public void TearDown()
    {
        _voss.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task A_saved_key_never_appears_in_any_response()
    {
        var saved = await _voss.PutAsJsonAsync("/api/me/ai-keys/openai", new { Key = Secret, Label = "Voss's key" });
        var list = await _voss.GetAsync("/api/me/ai-keys");
        var models = await _voss.GetAsync("/api/me/ai-models");

        Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        foreach (var body in new[] { await saved.Content.ReadAsStringAsync(), await list.Content.ReadAsStringAsync(), await models.Content.ReadAsStringAsync() })
        {
            Assert.That(body, Does.Not.Contain(Secret));
            Assert.That(body, Does.Not.Contain("sk-proj"), "not even a prefix");
        }

        var keys = await list.Content.ReadFromJsonAsync<List<AiKeyResponse>>();
        Assert.That(keys!.Single().LastFour, Is.EqualTo("7Q2x"));
        Assert.That(_factory.AiKeyChecker.Checks.Single().Key, Is.EqualTo(Secret), "checked with the provider before saving");
        Assert.That(await DbAsync(db => db.UserAiKeys.Select(k => k.ProtectedKey).SingleAsync()), Does.Not.Contain(Secret),
            "stored as ciphertext");
    }

    [TestCase(AiKeyCheckOutcome.Rejected, HttpStatusCode.BadRequest, "ai_key_check_failed")]
    [TestCase(AiKeyCheckOutcome.Unreachable, HttpStatusCode.ServiceUnavailable, "ai_key_check_unreachable")]
    public async Task A_key_that_fails_its_check_is_not_saved(AiKeyCheckOutcome outcome, HttpStatusCode status, string code)
    {
        _factory.AiKeyChecker.Outcome = outcome;

        var response = await _voss.PutAsJsonAsync("/api/me/ai-keys/openai", new { Key = Secret });

        Assert.That(response.StatusCode, Is.EqualTo(status));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain(code));
        Assert.That(await DbAsync(db => db.UserAiKeys.AnyAsync()), Is.False);
    }

    [Test]
    public async Task Another_users_key_is_unreachable()
    {
        await _voss.PutAsJsonAsync("/api/me/ai-keys/openai", new { Key = Secret });
        using var mire = _factory.CreateAuthenticatedClient(sub: MireSub, email: "mire@blackharbor.test");

        var list = await mire.GetFromJsonAsync<List<AiKeyResponse>>("/api/me/ai-keys");
        var delete = await mire.DeleteAsync("/api/me/ai-keys/openai");

        Assert.That(list, Is.Empty);
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await DbAsync(db => db.UserAiKeys.CountAsync()), Is.EqualTo(1), "Voss's key is still there");
    }

    [Test]
    public async Task Providers_without_a_client_yet_are_refused()
    {
        var response = await _voss.PutAsJsonAsync("/api/me/ai-keys/gemini", new { Key = Secret });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("provider_not_supported"));
        Assert.That(_factory.AiKeyChecker.Checks, Is.Empty);
    }

    [Test]
    public async Task Saving_is_limited_to_ten_checks_an_hour()
    {
        _factory.AiKeyChecker.Outcome = AiKeyCheckOutcome.Rejected;
        for (var i = 0; i < 10; i++)
        {
            await _voss.PutAsJsonAsync("/api/me/ai-keys/openai", new { Key = Secret });
        }

        var eleventh = await _voss.PutAsJsonAsync("/api/me/ai-keys/openai", new { Key = Secret });

        Assert.That(eleventh.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(_factory.AiKeyChecker.Checks, Has.Count.EqualTo(10));
    }

    [Test]
    public async Task Saving_a_key_queues_the_sources_that_failed_for_want_of_one()
    {
        var vossId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(_factory, VossSub, "voss@blackharbor.test");
        var mireId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(_factory, MireSub, "mire@blackharbor.test");
        var harbor = await SourceTestHelpers.CreateTestWorldAsync(_factory, vossId);
        var mireWorld = await SourceTestHelpers.CreateTestWorldAsync(_factory, mireId, name: "The Mire");
        var needsKey = await FailedSourceAsync(harbor.Id, vossId, ErrorCategories.AiKeyRequired);
        var rejected = await FailedSourceAsync(harbor.Id, vossId, ErrorCategories.AiKeyRejected);
        var broken = await FailedSourceAsync(harbor.Id, vossId, ErrorCategories.ValidationFailure);
        var notVosss = await FailedSourceAsync(mireWorld.Id, mireId, ErrorCategories.AiKeyRequired);

        var response = await _voss.PutAsJsonAsync("/api/me/ai-keys/openai", new { Key = Secret });

        var saved = await response.Content.ReadFromJsonAsync<SaveAiKeyResponse>();
        Assert.That(saved!.SourcesQueued, Is.EqualTo(2));
        Assert.That(_factory.ExtractionQueueClient.SentMessages.Select(m => m.SourceId), Is.EquivalentTo([needsKey, rejected]));
        var statuses = await DbAsync(db => db.Sources.ToDictionaryAsync(s => s.Id, s => (s.ProcessingStatus, s.FailureCategory)));
        Assert.Multiple(() =>
        {
            Assert.That(statuses[needsKey], Is.EqualTo((SourceProcessingStatus.Queued, (string?)null)), "the reason goes with the failure");
            Assert.That(statuses[broken].ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Failed), "a key can't fix this one");
            Assert.That(statuses[notVosss].ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Failed), "another creator's world");
        });
    }

    [Test]
    public async Task Models_list_the_catalog_and_default_to_the_first_model_a_key_covers()
    {
        var before = await _voss.GetFromJsonAsync<List<AiTaskModelsResponse>>("/api/me/ai-models");
        await _voss.PutAsJsonAsync("/api/me/ai-keys/openai", new { Key = Secret });
        var after = await _voss.GetFromJsonAsync<List<AiTaskModelsResponse>>("/api/me/ai-models");

        Assert.That(before!.Select(t => t.Task), Is.EquivalentTo(Enum.GetNames<AiTask>()));
        Assert.That(before!.All(t => t.DefaultId is null && t.Options.All(o => !o.HasKey)), Is.True);
        Assert.That(after!.Where(t => t.Task != nameof(AiTask.CoverArt)).All(t => t.DefaultId == "openai/gpt-5.4"), Is.True);
        Assert.That(after!.Single(t => t.Task == nameof(AiTask.CoverArt)).DefaultId, Is.EqualTo("openai/gpt-image-1"),
            "cover art runs on the image model, and only on it");
    }

    [Test]
    public async Task A_model_can_be_chosen_only_with_a_key_for_it()
    {
        var withoutKey = await _voss.PutAsJsonAsync("/api/me/ai-models/ask", new { ModelId = "openai/gpt-5.4" });
        await _voss.PutAsJsonAsync("/api/me/ai-keys/openai", new { Key = Secret });
        var unknown = await _voss.PutAsJsonAsync("/api/me/ai-models/ask", new { ModelId = "openai/no-such-model" });
        var chosen = await _voss.PutAsJsonAsync("/api/me/ai-models/ask", new { ModelId = "openai/gpt-5.4" });
        var models = await _voss.GetFromJsonAsync<List<AiTaskModelsResponse>>("/api/me/ai-models");

        Assert.That(withoutKey.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await withoutKey.Content.ReadAsStringAsync(), Does.Contain("no_key_for_model"));
        Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(chosen.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(models!.Single(t => t.Task == "Ask").ChosenId, Is.EqualTo("openai/gpt-5.4"));
    }

    [Test]
    public async Task The_trial_is_not_in_use_before_the_cutover()
    {
        var trial = await _voss.GetFromJsonAsync<AiTrialResponse>("/api/me/ai-trial");

        Assert.That(trial!.InUse, Is.False);
        Assert.That(trial.Available, Is.False);
        Assert.That(trial.AllowanceUsd, Is.EqualTo(2.00m));
    }

    [Test]
    public async Task World_funding_is_for_the_worlds_GMs_only()
    {
        var vossId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(_factory, VossSub, "voss@blackharbor.test");
        var mireId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(_factory, MireSub, "mire@blackharbor.test");
        var harbor = await SourceTestHelpers.CreateTestWorldAsync(_factory, vossId);
        await SourceTestHelpers.AddWorldMemberAsync(_factory, harbor.Id, mireId, WorldRole.Player);
        using var mire = _factory.CreateAuthenticatedClient(sub: MireSub, email: "mire@blackharbor.test");

        var gm = await _voss.GetFromJsonAsync<WorldAiFundingResponse>($"/api/worlds/{harbor.Id}/ai-funding");
        var player = await mire.GetAsync($"/api/worlds/{harbor.Id}/ai-funding");

        Assert.That(gm, Is.EqualTo(new WorldAiFundingResponse(false, null, true)));
        Assert.That(player.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    private async Task<Guid> FailedSourceAsync(Guid worldId, Guid userId, string category)
    {
        var source = await SourceTestHelpers.CreateTestSourceAsync(_factory, worldId, userId,
            title: $"Notes {category} {Guid.NewGuid():N}", processingStatus: SourceProcessingStatus.Failed, body: "Voss lied.");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var stored = await db.Sources.SingleAsync(s => s.Id == source.Id);
        stored.FailureCategory = category;
        await db.SaveChangesAsync();
        return source.Id;
    }

    private async Task<T> DbAsync<T>(Func<NornisDbContext, Task<T>> read)
    {
        using var scope = _factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<NornisDbContext>());
    }
}
