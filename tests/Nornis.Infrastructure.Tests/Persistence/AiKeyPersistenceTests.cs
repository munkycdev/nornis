using Microsoft.EntityFrameworkCore;
using Nornis.Application.Ai;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// Feature 39's storage, against a real change tracker: a source's failure reason lives exactly as
/// long as the failure, the requeue query finds only what a saved key can cure, and re-saving a
/// key keeps the line ids that model choices point at.
/// </summary>
[TestFixture]
public class AiKeyPersistenceTests : IntegrationTestBase
{
    private (User User, World World) SeedWorld(string name = "Vespergale")
    {
        var now = DateTimeOffset.UtcNow;
        var tag = Guid.NewGuid().ToString("N");
        var user = new User
        {
            Id = Guid.NewGuid(),
            SignIns = { new UserSignIn { Subject = $"auth0|{tag}" } },
            Username = $"gm-{tag}",
            Email = $"{tag}@example.com",
            CreatedAt = now,
            UpdatedAt = now,
            RowVersion = []
        };
        var world = new World
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = user.Id,
            RowVersion = []
        };
        using var context = CreateNewContext();
        context.Users.Add(user);
        context.Worlds.Add(world);
        context.SaveChanges();
        return (user, world);
    }

    private Source SeedSource(World world, SourceProcessingStatus status, string? category = null)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            Type = SourceType.SessionNote,
            Title = $"Session {Guid.NewGuid():N}",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = status,
            FailureCategory = category,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = world.CreatedByUserId,
        };
        using var context = CreateNewContext();
        context.Sources.Add(source);
        context.SaveChanges();
        return source;
    }

    [Test]
    public async Task A_failure_reason_is_dropped_by_any_other_status_whichever_writer_sets_it()
    {
        var (_, world) = SeedWorld();
        var repository = new SourceRepository(CreateNewContext());
        var viaStatus = SeedSource(world, SourceProcessingStatus.Queued);
        var viaUpdate = SeedSource(world, SourceProcessingStatus.Queued);

        await repository.MarkFailedAsync(viaStatus.Id, ErrorCategories.AiKeyRequired);
        await repository.MarkFailedAsync(viaUpdate.Id, ErrorCategories.AiKeyRequired);
        var failed = await CreateNewContext().Sources.AsNoTracking().FirstAsync(s => s.Id == viaStatus.Id);

        await repository.UpdateProcessingStatusAsync(viaStatus.Id, SourceProcessingStatus.Ready);
        var loaded = await CreateNewContext().Sources.AsNoTracking().FirstAsync(s => s.Id == viaUpdate.Id);
        loaded.ProcessingStatus = SourceProcessingStatus.Ready;
        await new SourceRepository(CreateNewContext()).UpdateAsync(loaded);

        var reread = await CreateNewContext().Sources.AsNoTracking()
            .Where(s => s.Id == viaStatus.Id || s.Id == viaUpdate.Id).Select(s => s.FailureCategory).ToListAsync();
        Assert.That(failed.FailureCategory, Is.EqualTo(ErrorCategories.AiKeyRequired));
        Assert.That(reread, Is.All.Null);
    }

    [Test]
    public async Task The_requeue_query_finds_key_failures_in_the_creators_worlds_only()
    {
        var (voss, harbor) = SeedWorld("Black Harbor");
        var (_, mire) = SeedWorld("The Mire");
        var needsKey = SeedSource(harbor, SourceProcessingStatus.Failed, ErrorCategories.AiKeyRequired);
        var excluded = SeedSource(harbor, SourceProcessingStatus.Failed, ErrorCategories.AiKeyRejected);
        SeedSource(harbor, SourceProcessingStatus.Failed, ErrorCategories.ParseFailure);
        SeedSource(harbor, SourceProcessingStatus.Failed);
        SeedSource(mire, SourceProcessingStatus.Failed, ErrorCategories.AiKeyRequired);

        var found = await new SourceRepository(CreateNewContext()).ListFailedInCreatedWorldsAsync(
            voss.Id, ErrorCategories.Refusals, take: 50, excluding: [excluded.Id]);

        Assert.That(found.Select(s => s.Id), Is.EqualTo([needsKey.Id]));
    }

    [Test]
    public async Task Resaving_a_key_keeps_the_line_of_a_task_still_listed_and_drops_the_rest()
    {
        var (voss, _) = SeedWorld();
        var repository = new AiKeyRepository(CreateNewContext());
        var key = Key(voss.Id, Line(AiTask.Ask, "llama-4"), Line(AiTask.Prep, "llama-4"));
        await repository.SaveAsync(key);
        var askLine = key.CompatibleModels.Single(l => l.Task == AiTask.Ask).Id;

        var replacement = Key(voss.Id, Line(AiTask.Ask, "llama-5"), Line(AiTask.Chores, "llama-5-mini"));
        replacement.Id = key.Id;
        replacement.LastFour = "9999";
        await repository.SaveAsync(replacement);

        var stored = await new AiKeyRepository(CreateNewContext()).GetAsync(key.Id);
        Assert.Multiple(() =>
        {
            Assert.That(stored!.LastFour, Is.EqualTo("9999"));
            Assert.That(stored.CompatibleModels.Select(l => (l.Task, l.ModelName)),
                Is.EquivalentTo([(AiTask.Ask, "llama-5"), (AiTask.Chores, "llama-5-mini")]));
            Assert.That(stored.CompatibleModels.Single(l => l.Task == AiTask.Ask).Id, Is.EqualTo(askLine));
        });
    }

    [Test]
    public async Task Choices_are_set_changed_and_cleared_and_a_key_goes_with_its_lines()
    {
        var (voss, _) = SeedWorld();
        var repository = new AiKeyRepository(CreateNewContext());
        var key = Key(voss.Id, Line(AiTask.Ask, "llama-4"));
        await repository.SaveAsync(key);

        await repository.SetChoiceAsync(voss.Id, AiTask.Ask, "openai/gpt-5.4");
        await repository.SetChoiceAsync(voss.Id, AiTask.Ask, "compatible:" + key.CompatibleModels[0].Id);
        var chosen = await repository.GetChoiceAsync(voss.Id, AiTask.Ask);
        await repository.SetChoiceAsync(voss.Id, AiTask.Ask, null);
        await repository.DeleteAsync(key.Id);

        using var check = CreateNewContext();
        var left = (await check.UserAiModelChoices.AnyAsync(), await check.UserAiKeys.AnyAsync(),
            await check.UserAiCompatibleModels.AnyAsync());
        Assert.That(chosen!.ModelId, Does.StartWith("compatible:"));
        Assert.That(left, Is.EqualTo((false, false, false)), "no choice, key or line is left");
    }

    private static UserAiKey Key(Guid userId, params UserAiCompatibleModel[] lines) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Provider = AiProvider.OpenAiCompatible,
        Label = "Home server",
        ProtectedKey = "ciphertext",
        LastFour = "1234",
        BaseUrl = "https://llm.example.com/v1/",
        AddedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        CheckedAt = DateTimeOffset.UtcNow,
        CompatibleModels = [.. lines],
    };

    private static UserAiCompatibleModel Line(AiTask task, string model) => new()
    {
        Id = Guid.NewGuid(),
        Task = task,
        ModelName = model,
        InputPerMillionUsd = 0.5m,
        OutputPerMillionUsd = 1.5m,
    };
}
