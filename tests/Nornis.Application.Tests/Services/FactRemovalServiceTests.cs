using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// GM-only removal of a single incorrect fact: the fact and its provenance rows are
/// deleted, and the GM's note becomes a GM-only GMNote source citing the fact's artifact,
/// so the correction itself stays on the record.
/// </summary>
[TestFixture]
public class FactRemovalServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmId = Guid.NewGuid();

    private InMemoryArtifactFactRepository _factRepository = null!;
    private InMemoryArtifactRepository _artifactRepository = null!;
    private InMemorySourceRepository _sourceRepository = null!;
    private InMemorySourceReferenceRepository _referenceRepository = null!;
    private FakeUnitOfWork _unitOfWork = null!;
    private FactRemovalService _sut = null!;

    private Guid _artifactId;
    private Guid _factId;

    [SetUp]
    public void SetUp()
    {
        _factRepository = new InMemoryArtifactFactRepository();
        _artifactRepository = new InMemoryArtifactRepository();
        _sourceRepository = new InMemorySourceRepository();
        _referenceRepository = new InMemorySourceReferenceRepository();
        _unitOfWork = new FakeUnitOfWork();

        _sut = new FactRemovalService(
            _factRepository, _artifactRepository, _sourceRepository, _referenceRepository,
            _unitOfWork, NullLogger<FactRemovalService>.Instance);

        _artifactId = Guid.NewGuid();
        _artifactRepository.Seed(new Artifact
        {
            Id = _artifactId,
            WorldId = WorldId,
            Type = ArtifactType.Character,
            Name = "Captain Voss",
            Status = ArtifactStatus.Active,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        _factId = Guid.NewGuid();
        _factRepository.Seed(new ArtifactFact
        {
            Id = _factId,
            ArtifactId = _artifactId,
            Predicate = "allegiance",
            Value = "the Iron Pact",
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        // The fact's own provenance — must be deleted with it.
        _referenceRepository.Seed(new SourceReference
        {
            Id = Guid.NewGuid(),
            SourceId = Guid.NewGuid(),
            TargetType = SourceReferenceTargetType.ArtifactFact,
            TargetId = _factId,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private RemoveFactCommand Command(WorldRole role = WorldRole.GM, string note = "Session 12 retconned this.") =>
        new(WorldId, _factId, note, GmId, role);

    [Test]
    public async Task Remove_DeletesFactAndItsProvenance()
    {
        var result = await _sut.RemoveAsync(Command(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_factRepository.Facts, Is.Empty);
        Assert.That(_referenceRepository.References.Where(r => r.TargetType == SourceReferenceTargetType.ArtifactFact), Is.Empty);
    }

    [Test]
    public async Task Remove_RecordsAGmNoteCitingTheArtifact()
    {
        var result = await _sut.RemoveAsync(Command(note: "Voss never joined the Pact."), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var note = _sourceRepository.Sources.Single(s => s.Type == SourceType.GMNote);
        Assert.That(note.WorldId, Is.EqualTo(WorldId));
        Assert.That(note.Visibility, Is.EqualTo(VisibilityScope.GMOnly));
        Assert.That(note.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Processed));
        Assert.That(note.ExtractionEnabled, Is.False);
        Assert.That(note.Title, Does.Contain("Captain Voss").And.Contain("allegiance"));
        Assert.That(note.Body, Does.Contain("Voss never joined the Pact.").And.Contain("the Iron Pact"));

        var citation = _referenceRepository.References.Single(r => r.SourceId == note.Id);
        Assert.That(citation.TargetType, Is.EqualTo(SourceReferenceTargetType.Artifact));
        Assert.That(citation.TargetId, Is.EqualTo(_artifactId));
    }

    [Test]
    public async Task Remove_NonGm_Returns403_AndChangesNothing()
    {
        var result = await _sut.RemoveAsync(Command(role: WorldRole.Player), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(_factRepository.Facts, Has.Count.EqualTo(1));
        Assert.That(_sourceRepository.Sources, Is.Empty);
    }

    [Test]
    public async Task Remove_BlankNote_Returns400()
    {
        var result = await _sut.RemoveAsync(Command(note: "   "), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(_factRepository.Facts, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Remove_UnknownFact_Returns404()
    {
        var result = await _sut.RemoveAsync(
            new RemoveFactCommand(WorldId, Guid.NewGuid(), "note", GmId, WorldRole.GM), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task Remove_FactFromAnotherWorld_Returns404()
    {
        var result = await _sut.RemoveAsync(
            new RemoveFactCommand(Guid.NewGuid(), _factId, "note", GmId, WorldRole.GM), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(_factRepository.Facts, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Remove_CommitFailure_RollsBackAndReports500()
    {
        _unitOfWork.ConfigureCommitFailure();

        var result = await _sut.RemoveAsync(Command(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(500));
        Assert.That(_unitOfWork.Transactions.Single().RolledBack, Is.True);
    }
}
