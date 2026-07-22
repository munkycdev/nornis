using Microsoft.Extensions.Logging;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Removes a single incorrect fact from canon — the per-fact counterpart to
/// <see cref="ArtifactRemovalService"/>. The GM's note becomes a GM-only GMNote source
/// referencing the fact's artifact, so "why did this fact disappear" stays answerable,
/// and the fact's own provenance rows are deleted with it.
/// </summary>
public class FactRemovalService : IFactRemovalService
{
    private readonly IArtifactFactRepository _artifactFactRepository;
    private readonly IArtifactRepository _artifactRepository;
    private readonly ISourceRepository _sourceRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<FactRemovalService> _logger;

    public FactRemovalService(
        IArtifactFactRepository artifactFactRepository,
        IArtifactRepository artifactRepository,
        ISourceRepository sourceRepository,
        ISourceReferenceRepository sourceReferenceRepository,
        IUnitOfWork unitOfWork,
        ILogger<FactRemovalService> logger)
    {
        _artifactFactRepository = artifactFactRepository;
        _artifactRepository = artifactRepository;
        _sourceRepository = sourceRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<AppResult> RemoveAsync(RemoveFactCommand command, CancellationToken ct)
    {
        if (command.ActingUserRole != WorldRole.GM)
        {
            return AppResult.Fail(new AppError(403, "forbidden", "Only the GM can remove facts from canon."));
        }

        if (string.IsNullOrWhiteSpace(command.Note))
        {
            return AppResult.Fail(new AppError(400, "validation_error",
                "A note explaining the removal is required — it becomes the record of the correction."));
        }

        var fact = await _artifactFactRepository.GetByIdAsync(command.FactId, ct);
        if (fact is null)
        {
            return AppResult.Fail(new AppError(404, "not_found", "Fact not found."));
        }

        var artifact = await _artifactRepository.GetByIdAsync(fact.ArtifactId, ct);
        if (artifact is null || artifact.WorldId != command.WorldId)
        {
            return AppResult.Fail(new AppError(404, "not_found", "Fact not found."));
        }

        var now = DateTimeOffset.UtcNow;
        var note = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = command.WorldId,
            Type = SourceType.GMNote,
            Title = Truncate($"Fact removed — {artifact.Name}: {fact.Predicate} — {now:yyyy-MM-dd}", 200),
            Body = $"GM removed an incorrect fact from \"{artifact.Name}\".\n\n" +
                   $"Fact: {fact.Predicate} — {fact.Value} ({fact.TruthState})\n\n" +
                   $"Reason: {command.Note.Trim()}",
            Visibility = VisibilityScope.GMOnly,
            ProcessingStatus = SourceProcessingStatus.Processed,
            ExtractionEnabled = false,
            CreatedAt = now,
            CreatedByUserId = command.ActingUserId
        };

        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _sourceRepository.CreateAsync(note, ct);

            // The note cites the artifact the fact hung off, so the correction shows up in
            // that artifact's source trail.
            await _sourceReferenceRepository.CreateAsync(new SourceReference
            {
                Id = Guid.NewGuid(),
                SourceId = note.Id,
                TargetType = SourceReferenceTargetType.Artifact,
                TargetId = artifact.Id,
                Notes = Truncate($"Removed fact: {fact.Predicate} — {fact.Value}", 2000),
                CreatedAt = now
            }, ct);

            await _artifactFactRepository.DeleteAsync(fact.Id, ct);
            await _sourceReferenceRepository.DeleteByTargetAsync(
                SourceReferenceTargetType.ArtifactFact, fact.Id, ct);

            await transaction.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex,
                "Fact removal failed. FactId={FactId}, WorldId={WorldId}", command.FactId, command.WorldId);
            return AppResult.Fail(new AppError(500, "transaction_failed",
                "Failed to remove the fact. No changes were made."));
        }

        _logger.LogInformation(
            "Fact removed from canon. FactId={FactId}, ArtifactId={ArtifactId}, WorldId={WorldId}, NoteSourceId={NoteSourceId}",
            command.FactId, artifact.Id, command.WorldId, note.Id);

        return AppResult.Success();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
