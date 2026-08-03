using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Nornis.Application.Application;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Application.Tests.Generators;
using Nornis.Application.Validation;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Properties;

/// <summary>
/// The setup the review property fixtures share. <see cref="JsonOptions"/> arrived here from four
/// byte-identical copies, one per numbered file; the rest were private to a single file and are
/// shared now only because the properties that used them were split across fixtures by concern.
/// </summary>
internal static class ReviewPropertySupport
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    internal static readonly JsonSerializerOptions JsonOptionsInsensitive = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// The shared harness plus a generated scenario's rows. The construction these builders used
    /// to inline moved to <see cref="ReviewHarness"/>; the seeding below is what stayed behind,
    /// because it is the only part that was ever specific to the property fixtures.
    /// </summary>
    internal static ReviewHarness SeededWithFakes(ReviewScenario scenario)
    {
        var harness = ReviewHarness.WithFakeApplicator();
        harness.SourceRepo.Seed(scenario.Sources);
        foreach (var batch in scenario.Batches)
        {
            harness.BatchRepo.CreateAsync(batch).GetAwaiter().GetResult();
        }
        foreach (var proposal in scenario.Proposals)
        {
            harness.ProposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();
        }
        return harness;
    }

    /// <summary>
    /// The real validator and applicator, for the properties whose subject is what a payload
    /// turns into rather than how ReviewService routes it.
    /// </summary>
    internal static ReviewHarness SeededWithRealApplicator(ProposalWithContext ctx)
    {
        var harness = ReviewHarness.WithRealApplicator();
        harness.SourceRepo.Seed(ctx.Source);
        harness.BatchRepo.CreateAsync(ctx.Batch).GetAwaiter().GetResult();
        harness.ProposalRepo.CreateAsync(ctx.Proposal).GetAwaiter().GetResult();
        return harness;
    }

    /// <summary>
    /// Seeds the standard source + batch + proposal structure and returns relevant IDs.
    /// </summary>
    internal static (Source Source, ReviewBatch Batch, Guid WorldId, Guid UserId)
        SeedSourceAndBatch(ReviewHarness ctx)
    {
        var userId = Guid.NewGuid();
        var worldId = Guid.NewGuid();
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Type = SourceType.SessionNote,
            Title = "Test Source",
            Body = "Content about Captain Voss",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedByUserId = userId,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed
        };
        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            SourceId = source.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = source.CreatedAt.AddMinutes(5)
        };
        ctx.SourceRepo.Seed(source);
        ctx.BatchRepo.CreateAsync(batch).GetAwaiter().GetResult();
        return (source, batch, worldId, userId);
    }

    /// <summary>
    /// DTO for deserializing CreateArtifact payloads in assertions.
    /// </summary>
    internal record CreateArtifactPayloadDto(
        string Name,
        string Type,
        string? Summary,
        string? Visibility,
        decimal? Confidence);

    internal record AddFactPayloadDto(
            string Predicate,
            string Value,
            decimal? Confidence,
            string? TruthState,
            string? Visibility);
}
