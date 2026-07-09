using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class CampaignServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmUserId = Guid.NewGuid();

    private InMemoryCampaignRepository _campaignRepository = null!;
    private InMemoryCharacterRepository _characterRepository = null!;
    private InMemorySourceRepository _sourceRepository = null!;
    private CampaignService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _characterRepository = new InMemoryCharacterRepository();
        _campaignRepository = new InMemoryCampaignRepository(_sourceRepository, _characterRepository);
        _sut = new CampaignService(_campaignRepository, _characterRepository);
    }

    private static Campaign CreateCampaign(Guid? worldId = null, string name = "Missing Caravan Arc") => new()
    {
        Id = Guid.NewGuid(),
        WorldId = worldId ?? WorldId,
        Name = name,
        Status = CampaignStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        CreatedByUserId = GmUserId
    };

    private static Character CreateCharacter(Guid? worldId = null, string name = "Tavrin") => new()
    {
        Id = Guid.NewGuid(),
        WorldId = worldId ?? WorldId,
        WorldMemberId = Guid.NewGuid(),
        Name = name,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    // ------------------------------------------------------------------- Create --

    [Test]
    public async Task CreateAsync_AsGm_CreatesCampaign()
    {
        var command = new CreateCampaignCommand(WorldId, "Missing Caravan Arc", GmUserId, WorldRole.GM,
            Description: "The caravan mystery", StartedAt: DateTimeOffset.UtcNow.AddDays(-30));

        var result = await _sut.CreateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Name, Is.EqualTo("Missing Caravan Arc"));
        Assert.That(result.Value.WorldId, Is.EqualTo(WorldId));
        Assert.That(result.Value.Status, Is.EqualTo(CampaignStatus.Active));
        Assert.That(_campaignRepository.Campaigns, Has.Count.EqualTo(1));
    }

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task CreateAsync_AsNonGm_Returns403(WorldRole role)
    {
        var command = new CreateCampaignCommand(WorldId, "Side Game", Guid.NewGuid(), role);

        var result = await _sut.CreateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(_campaignRepository.Campaigns, Is.Empty);
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task CreateAsync_BlankName_Returns400(string name)
    {
        var command = new CreateCampaignCommand(WorldId, name, GmUserId, WorldRole.GM);

        var result = await _sut.CreateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task CreateAsync_NameOver200Chars_Returns400()
    {
        var command = new CreateCampaignCommand(WorldId, new string('x', 201), GmUserId, WorldRole.GM);

        var result = await _sut.CreateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task CreateAsync_EndBeforeStart_Returns400()
    {
        var command = new CreateCampaignCommand(WorldId, "Backwards", GmUserId, WorldRole.GM,
            StartedAt: DateTimeOffset.UtcNow,
            EndedAt: DateTimeOffset.UtcNow.AddDays(-1));

        var result = await _sut.CreateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    // ------------------------------------------------------------------ Get/List --

    [Test]
    public async Task GetByIdAsync_WrongWorld_Returns404()
    {
        var campaign = CreateCampaign();
        _campaignRepository.Seed(campaign);

        var result = await _sut.GetByIdAsync(campaign.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task ListByWorldAsync_ReturnsOnlyThisWorldsCampaigns()
    {
        _campaignRepository.Seed(CreateCampaign(), CreateCampaign(worldId: Guid.NewGuid(), name: "Other World Game"));

        var result = await _sut.ListByWorldAsync(WorldId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Has.Count.EqualTo(1));
        Assert.That(result.Value![0].Name, Is.EqualTo("Missing Caravan Arc"));
    }

    // ------------------------------------------------------------------- Update --

    [Test]
    public async Task UpdateAsync_AsGm_AppliesOnlyProvidedFields()
    {
        var campaign = CreateCampaign();
        _campaignRepository.Seed(campaign);

        var command = new UpdateCampaignCommand(campaign.Id, WorldId, GmUserId, WorldRole.GM,
            Status: CampaignStatus.Completed);

        var result = await _sut.UpdateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Status, Is.EqualTo(CampaignStatus.Completed));
        Assert.That(result.Value.Name, Is.EqualTo("Missing Caravan Arc"));
    }

    [Test]
    public async Task UpdateAsync_AsPlayer_Returns403()
    {
        var campaign = CreateCampaign();
        _campaignRepository.Seed(campaign);

        var command = new UpdateCampaignCommand(campaign.Id, WorldId, Guid.NewGuid(), WorldRole.Player, Name: "Hijacked");

        var result = await _sut.UpdateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task UpdateAsync_WrongWorld_Returns404()
    {
        var campaign = CreateCampaign();
        _campaignRepository.Seed(campaign);

        var command = new UpdateCampaignCommand(campaign.Id, Guid.NewGuid(), GmUserId, WorldRole.GM, Name: "New Name");

        var result = await _sut.UpdateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    // ------------------------------------------------------------------- Delete --

    [Test]
    public async Task DeleteAsync_DetachesSourcesAndAssignments_KeepsSources()
    {
        var campaign = CreateCampaign();
        var character = CreateCharacter();
        _campaignRepository.Seed(campaign);
        _characterRepository.Seed(character);
        _characterRepository.SeedAssignments(new CampaignCharacter
        {
            Id = Guid.NewGuid(),
            CampaignId = campaign.Id,
            CharacterId = character.Id,
            CreatedAt = DateTimeOffset.UtcNow
        });
        _sourceRepository.Seed(new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            CampaignId = campaign.Id,
            Type = SourceType.SessionNote,
            Title = "Session 1",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = GmUserId
        });

        var result = await _sut.DeleteAsync(campaign.Id, WorldId, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_campaignRepository.Campaigns, Is.Empty);
        Assert.That(_sourceRepository.Sources, Has.Count.EqualTo(1), "sources must survive campaign deletion");
        Assert.That(_sourceRepository.Sources[0].CampaignId, Is.Null);
        Assert.That(_characterRepository.Assignments, Is.Empty);
        Assert.That(_characterRepository.Characters, Has.Count.EqualTo(1), "characters must survive campaign deletion");
    }

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task DeleteAsync_AsNonGm_Returns403(WorldRole role)
    {
        var campaign = CreateCampaign();
        _campaignRepository.Seed(campaign);

        var result = await _sut.DeleteAsync(campaign.Id, WorldId, Guid.NewGuid(), role, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(_campaignRepository.Campaigns, Has.Count.EqualTo(1));
    }

    // -------------------------------------------------------- Character assignment --

    [Test]
    public async Task AssignCharactersAsync_ReplacesAssignmentSet()
    {
        var campaign = CreateCampaign();
        var keep = CreateCharacter(name: "Tavrin");
        var add = CreateCharacter(name: "Jorin");
        var remove = CreateCharacter(name: "Old Timer");
        _campaignRepository.Seed(campaign);
        _characterRepository.Seed(keep, add, remove);
        await _characterRepository.ReplaceCampaignAssignmentsAsync(campaign.Id, [keep.Id, remove.Id]);

        var command = new AssignCampaignCharactersCommand(campaign.Id, WorldId, GmUserId, WorldRole.GM, [keep.Id, add.Id]);

        var result = await _sut.AssignCharactersAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Select(c => c.Name), Is.EquivalentTo(new[] { "Tavrin", "Jorin" }));
    }

    [Test]
    public async Task AssignCharactersAsync_CharacterFromAnotherWorld_Returns400()
    {
        var campaign = CreateCampaign();
        var foreignCharacter = CreateCharacter(worldId: Guid.NewGuid());
        _campaignRepository.Seed(campaign);
        _characterRepository.Seed(foreignCharacter);

        var command = new AssignCampaignCharactersCommand(campaign.Id, WorldId, GmUserId, WorldRole.GM, [foreignCharacter.Id]);

        var result = await _sut.AssignCharactersAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(_characterRepository.Assignments, Is.Empty);
    }

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task AssignCharactersAsync_AsNonGm_Returns403(WorldRole role)
    {
        var campaign = CreateCampaign();
        var character = CreateCharacter();
        _campaignRepository.Seed(campaign);
        _characterRepository.Seed(character);

        var command = new AssignCampaignCharactersCommand(campaign.Id, WorldId, Guid.NewGuid(), role, [character.Id]);

        var result = await _sut.AssignCharactersAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task AssignCharactersAsync_DuplicateIds_AssignsOnce()
    {
        var campaign = CreateCampaign();
        var character = CreateCharacter();
        _campaignRepository.Seed(campaign);
        _characterRepository.Seed(character);

        var command = new AssignCampaignCharactersCommand(campaign.Id, WorldId, GmUserId, WorldRole.GM,
            [character.Id, character.Id]);

        var result = await _sut.AssignCharactersAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_characterRepository.Assignments, Has.Count.EqualTo(1));
    }
}
