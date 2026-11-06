using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// An Adventure's side conversation (feature 30) against SQLite with foreign keys enforced: one
/// per Adventure, messages in time order, Clear touching nothing else, and the conversation going
/// with its Adventure (its FK is Restrict, so a missing delete is a constraint violation here, as
/// it would be in production). Also the world export's Prep category carrying it.
/// </summary>
[TestFixture]
public class ConversationRepositoryTests : IntegrationTestBase
{
    private ConversationRepository _sut = null!;
    private User _user = null!;
    private World _world = null!;
    private DateTimeOffset _now;

    [SetUp]
    public async Task SetUp()
    {
        _sut = new ConversationRepository(Context);
        _now = DateTimeOffset.UtcNow;
        var tag = Guid.NewGuid().ToString("N");
        _user = new User
        {
            Id = Guid.NewGuid(),
            SignIns = { new UserSignIn { Subject = $"auth0|{tag}" } },
            Username = $"gm-{tag}",
            Email = $"{tag}@example.com",
            CreatedAt = _now,
            UpdatedAt = _now,
        };
        _world = new World { Id = Guid.NewGuid(), Name = "Black Harbor", CreatedByUserId = _user.Id, CreatedAt = _now, UpdatedAt = _now };
        Context.AddRange(_user, _world);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
    }

    private async Task<Adventure> CreateAdventureAsync(string title = "The Silver Key")
    {
        var adventure = new Adventure
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            Title = title,
            CreatedByUserId = _user.Id,
            CreatedAt = _now,
            UpdatedAt = _now,
        };
        adventure.Sections =
        [
            new AdventureSection
            {
                Id = Guid.NewGuid(),
                AdventureId = adventure.Id,
                Kind = AdventureSectionKind.Scene,
                Title = "Scene 1",
                BodyMarkdown = "> Fog.",
                SortOrder = 1,
                UpdatedAt = _now,
            },
        ];
        await new AdventureRepository(Context).CreateAsync(adventure);
        Context.ChangeTracker.Clear();
        return adventure;
    }

    private Conversation NewConversation(Adventure adventure, params (ConversationRole Role, string Content, int Seconds)[] messages)
    {
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            UserId = _user.Id,
            AdventureId = adventure.Id,
            Title = adventure.Title,
            CreatedAt = _now,
            UpdatedAt = _now,
        };
        conversation.Messages = messages.Select(m => new ConversationMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Role = m.Role,
            Content = m.Content,
            Model = m.Role == ConversationRole.Assistant ? "gpt-test" : null,
            CreatedAt = _now.AddSeconds(m.Seconds),
        }).ToList();
        return conversation;
    }

    [Test]
    public async Task GetByAdventure_ReturnsTheMessages_OldestFirst()
    {
        var adventure = await CreateAdventureAsync();
        await _sut.CreateAsync(NewConversation(adventure,
            (ConversationRole.Assistant, "second", 2), (ConversationRole.User, "first", 1), (ConversationRole.User, "third", 3)));

        var loaded = await _sut.GetByAdventureAsync(adventure.Id);

        Assert.That(loaded!.Messages.Select(m => m.Content), Is.EqualTo(["first", "second", "third"]));
    }

    [Test]
    public async Task AQuestionAndItsAnswerInTheSameTick_PutTheQuestionFirst()
    {
        var adventure = await CreateAdventureAsync();
        var conversation = NewConversation(adventure);
        await _sut.CreateAsync(conversation);
        // Saved one at a time, the answer first, so the rows sit in the wrong order on disk.
        foreach (var (role, content) in new[] { (ConversationRole.Assistant, "answer"), (ConversationRole.User, "question") })
        {
            await _sut.AddMessageAsync(new ConversationMessage
            {
                Id = Guid.NewGuid(),
                ConversationId = conversation.Id,
                Role = role,
                Content = content,
                CreatedAt = _now,
            });
        }

        var loaded = await _sut.GetByAdventureAsync(adventure.Id);

        Assert.That(loaded!.Messages.Select(m => m.Content), Is.EqualTo(["question", "answer"]));
    }

    [Test]
    public async Task GetByAdventure_IsNull_BeforeTheFirstMessage()
    {
        var adventure = await CreateAdventureAsync();

        Assert.That(await _sut.GetByAdventureAsync(adventure.Id), Is.Null);
    }

    [Test]
    public async Task AnAdventure_HoldsAtMostOneConversation()
    {
        var adventure = await CreateAdventureAsync();
        await _sut.CreateAsync(NewConversation(adventure));

        Assert.That(async () => await _sut.CreateAsync(NewConversation(adventure)), Throws.InstanceOf<DbUpdateException>());
        Context.ChangeTracker.Clear(); // the refused row stays tracked, and the next test's save would retry it
    }

    [Test]
    public async Task AddMessage_AppendsIt_AndBumpsTheConversationsStamp()
    {
        var adventure = await CreateAdventureAsync();
        var conversation = NewConversation(adventure, (ConversationRole.User, "first", 1));
        await _sut.CreateAsync(conversation);
        var at = _now.AddMinutes(5);

        await _sut.AddMessageAsync(new ConversationMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Role = ConversationRole.Assistant,
            Content = "an answer",
            Model = "gpt-test",
            CreatedAt = at,
        });

        var loaded = await _sut.GetByAdventureAsync(adventure.Id);
        Assert.That(loaded!.Messages.Select(m => m.Content), Is.EqualTo(["first", "an answer"]));
        Assert.That(loaded.UpdatedAt, Is.EqualTo(at));
    }

    [Test]
    public async Task AResearchAnswer_KeepsItsWebPages_AndOtherMessagesKeepNone()
    {
        var adventure = await CreateAdventureAsync();
        var conversation = NewConversation(adventure, (ConversationRole.User, "a question", 1));
        await _sut.CreateAsync(conversation);
        var research = WebResearchResult.Create(
            [new WebPage("Waterdeep, City of Splendors", "https://example.org/waterdeep"), new WebPage("", "https://example.com/wards")],
            searches: 2,
            suggestionsHtml: "<div>waterdeep wards</div>");
        await _sut.AddMessageAsync(new ConversationMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Role = ConversationRole.Assistant,
            Content = "Waterdeep is divided into wards.",
            Model = "gpt-test",
            WebResearch = research,
            CreatedAt = _now.AddSeconds(2),
        });
        Context.ChangeTracker.Clear();

        var loaded = (await _sut.GetByAdventureAsync(adventure.Id))!.Messages.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(loaded[0].WebResearch, Is.Null, "null: not a research turn");
            Assert.That(loaded[1].WebResearch!.Pages, Is.EqualTo(research.Pages));
            Assert.That(loaded[1].WebResearch!.Pages[1].Title, Is.EqualTo("example.com"));
            Assert.That(loaded[1].WebResearch!.Searches, Is.EqualTo(2));
            Assert.That(loaded[1].WebResearch!.SuggestionsHtml, Is.EqualTo("<div>waterdeep wards</div>"));
        });
    }

    [Test]
    public async Task ADesignAnswer_KeepsItsEditOutcomes_AndAHandApplyReplacesThem()
    {
        var adventure = await CreateAdventureAsync();
        var conversation = NewConversation(adventure, (ConversationRole.User, "a question", 1));
        await _sut.CreateAsync(conversation);
        var answerId = Guid.NewGuid();
        await _sut.AddMessageAsync(new ConversationMessage
        {
            Id = answerId,
            ConversationId = conversation.Id,
            Role = ConversationRole.Assistant,
            Content = "Two edits.",
            DraftEditOutcomes = [DraftEditOutcome.Applied, DraftEditOutcome.DidNotFit],
            CreatedAt = _now.AddSeconds(2),
        });
        Context.ChangeTracker.Clear();

        var loaded = (await _sut.GetByAdventureAsync(adventure.Id))!.Messages.ToList();
        Assert.Multiple(() =>
        {
            Assert.That(loaded[0].DraftEditOutcomes, Is.Null, "null: not a Design answer");
            Assert.That(loaded[1].DraftEditOutcomes, Is.EqualTo([DraftEditOutcome.Applied, DraftEditOutcome.DidNotFit]));
        });

        await _sut.SetDraftEditOutcomesAsync(answerId, [DraftEditOutcome.Applied, DraftEditOutcome.Applied]);

        Assert.That((await _sut.GetMessageAsync(answerId))!.DraftEditOutcomes,
            Is.EqualTo([DraftEditOutcome.Applied, DraftEditOutcome.Applied]));
    }

    [Test]
    public async Task GetMessage_CarriesItsConversation()
    {
        var adventure = await CreateAdventureAsync();
        var conversation = NewConversation(adventure, (ConversationRole.Assistant, "an answer", 1));
        await _sut.CreateAsync(conversation);

        var message = await _sut.GetMessageAsync(conversation.Messages.Single().Id);

        Assert.That(message!.Conversation.AdventureId, Is.EqualTo(adventure.Id));
    }

    [Test]
    public async Task DeleteByAdventure_ClearsThatConversation_AndNoOther()
    {
        var adventure = await CreateAdventureAsync();
        var other = await CreateAdventureAsync("The Drowned Bell");
        await _sut.CreateAsync(NewConversation(adventure, (ConversationRole.User, "mine", 1)));
        await _sut.CreateAsync(NewConversation(other, (ConversationRole.User, "theirs", 1)));

        await _sut.DeleteByAdventureAsync(adventure.Id);

        Assert.That(await _sut.GetByAdventureAsync(adventure.Id), Is.Null);
        Assert.That((await _sut.GetByAdventureAsync(other.Id))!.Messages.Single().Content, Is.EqualTo("theirs"));
        Assert.That(await Context.AdventureSections.CountAsync(s => s.AdventureId == adventure.Id), Is.EqualTo(1),
            "Clear touches the conversation only");
    }

    [Test]
    public async Task DeletingTheAdventure_DeletesItsConversation()
    {
        var adventure = await CreateAdventureAsync();
        var conversation = NewConversation(adventure, (ConversationRole.User, "first", 1), (ConversationRole.Assistant, "an answer", 2));
        await _sut.CreateAsync(conversation);

        await new AdventureRepository(Context).DeleteAsync(adventure.Id);
        Context.ChangeTracker.Clear();

        Assert.That(await Context.Adventures.CountAsync(a => a.Id == adventure.Id), Is.Zero);
        Assert.That(await Context.Conversations.CountAsync(c => c.Id == conversation.Id), Is.Zero);
        Assert.That(await Context.ConversationMessages.CountAsync(m => m.ConversationId == conversation.Id), Is.Zero);
    }

    [Test]
    public async Task TheWorldExport_CarriesThisWorldsConversations()
    {
        var adventure = await CreateAdventureAsync();
        var conversation = NewConversation(adventure, (ConversationRole.User, "first", 1), (ConversationRole.Assistant, "an answer", 2));
        await _sut.CreateAsync(conversation);

        var data = await new WorldExportReader(Context).ReadAsync(_world.Id, new HashSet<WorldExportCategory> { WorldExportCategory.Prep });
        var without = await new WorldExportReader(Context).ReadAsync(_world.Id, new HashSet<WorldExportCategory> { WorldExportCategory.Sources });

        Assert.That(data.Conversations.Select(c => c.AdventureId), Is.EqualTo([adventure.Id]));
        Assert.That(data.ConversationMessages.Select(m => m.Content), Is.EqualTo(["first", "an answer"]));
        Assert.That(without.Conversations, Is.Empty);
        Assert.That(without.ConversationMessages, Is.Empty);
    }
}
