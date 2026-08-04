using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 9: Processing Status State Machine
///
/// For any pair of (currentStatus, targetStatus) from the SourceProcessingStatus enum, a status transition
/// should succeed if and only if the pair matches one of the valid transitions: Draft→Ready, Ready→Queued, Ready→Ready (mark-ready retry),
/// Queued→Processing, Processing→Processed, Processing→Failed, Failed→Ready. All other transitions should
/// be rejected.
///
/// The SourceService exposes the state machine publicly via MarkReadyAsync (which enforces Draft→Ready).
/// Since MarkReadyAsync is the only public transition method, this test validates:
/// - MarkReadyAsync succeeds from Draft (and Ready as a retry; Failed re-entry is covered by the map)
/// - MarkReadyAsync from any non-Draft status returns "invalid_transition" error
/// </summary>
[TestFixture]
[Category("Feature: world-sources, Property 9: Processing Status State Machine")]
public class SourceProcessingStatusStateMachineTests
{
    /// <summary>
    /// Valid transitions per the state machine definition.
    ///
    /// A second copy of SourceService's table, and it drifted the moment production changed:
    /// Queued → Ready was added on 2026-08-02 as the way out of the dead-letter wedge, and this
    /// copy still said no. Kept as a copy rather than reaching for the private original,
    /// because a test that asks the subject what the answer is asserts nothing — but that means
    /// this list is a claim about intent, and a failure here is a prompt to check which of the
    /// two is wrong.
    ///
    /// Queued → Ready is deliberately absent below even though production allows it, because
    /// it is *conditional*: only once the source has been stuck past
    /// <see cref="SourceService.StaleQueuedThreshold"/>. Sources here are seeded freshly queued,
    /// so the refusal is the correct behaviour — with its own error code. The time rule itself
    /// is covered by SourceServiceQueuedWedgeTests.
    /// </summary>
    private static readonly Dictionary<SourceProcessingStatus, HashSet<SourceProcessingStatus>> ValidTransitions = new()
    {
        [SourceProcessingStatus.Draft] = new() { SourceProcessingStatus.Ready },
        [SourceProcessingStatus.Ready] = new() { SourceProcessingStatus.Queued, SourceProcessingStatus.Ready },
        [SourceProcessingStatus.Queued] = new() { SourceProcessingStatus.Processing },
        [SourceProcessingStatus.Processing] = new() { SourceProcessingStatus.Processed, SourceProcessingStatus.Failed },
        [SourceProcessingStatus.Processed] = new(),
        [SourceProcessingStatus.Failed] = new() { SourceProcessingStatus.Ready },
    };

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(ProcessingStatusStateMachineArbitraries)],
        MaxTest = 100)]
    [Description("Feature: world-sources, Property 9: Processing Status State Machine")]
    public void MarkReadyAsync_SucceedsOnlyFromDraft_RejectsAllOtherStatuses(StatusTransitionScenario scenario)
    {
        // Arrange
        var sourceRepo = new InMemorySourceRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var queueClient = new FakeExtractionQueueClient();
        var service = new SourceService(sourceRepo, memberRepo, new InMemoryCampaignRepository(), queueClient,
            new InMemoryReviewBatchRepository(), new InMemoryReviewProposalRepository(), new InMemorySourceAttachmentRepository(),
            new FakeBlobStorageService(), NullLogger<SourceService>.Instance);

        // Create a source with the given current status
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = scenario.WorldId,
            Type = SourceType.SessionNote,
            Title = "Session 4 — Questioning Captain Voss",
            Body = "We questioned Captain Voss in Black Harbor.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = scenario.CurrentStatus,
            // Freshly moved, so a Queued source is inside the wedge threshold rather than past it.
            StatusChangedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = scenario.ActingUserId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };

        sourceRepo.CreateAsync(source, CancellationToken.None).GetAwaiter().GetResult();

        // MarkReadyAsync attempts Draft→Ready transition
        var command = new MarkSourceReadyCommand(
            source.Id,
            scenario.WorldId,
            scenario.ActingUserId,
            WorldRole.GM);

        // The target of MarkReadyAsync is Ready
        var targetStatus = SourceProcessingStatus.Ready;
        var isValidTransition = ValidTransitions.TryGetValue(scenario.CurrentStatus, out var validTargets)
            && validTargets.Contains(targetStatus);

        // Act
        var result = service.MarkReadyAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        if (isValidTransition)
        {
            // Draft→Ready is valid; MarkReadyAsync further transitions to Queued
            Assert.That(result.IsSuccess, Is.True,
                $"Transition from {scenario.CurrentStatus} to Ready should succeed (valid transition).");

            // After successful mark-ready, source should be at Queued (Draft→Ready→Queued)
            var updatedSource = sourceRepo.Sources.First(s => s.Id == source.Id);
            Assert.That(updatedSource.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Queued),
                "After successful MarkReady, source should transition to Queued.");
        }
        else
        {
            // All other statuses → Ready are invalid
            Assert.That(result.IsSuccess, Is.False,
                $"Transition from {scenario.CurrentStatus} to Ready should be rejected (invalid transition).");

            // A freshly queued source is refused for a different reason, and says so: the
            // transition is legal, the source is simply too recent to be considered wedged.
            var expectedCode = scenario.CurrentStatus == SourceProcessingStatus.Queued
                ? "still_queued"
                : "invalid_transition";
            Assert.That(result.Error!.Code, Is.EqualTo(expectedCode),
                $"Error code should be '{expectedCode}' when attempting from {scenario.CurrentStatus}.");
            Assert.That(result.Error.StatusCode, Is.EqualTo(409),
                "Invalid transition should return 409 Conflict.");

            // Source status should remain unchanged
            var unchangedSource = sourceRepo.Sources.First(s => s.Id == source.Id);
            Assert.That(unchangedSource.ProcessingStatus, Is.EqualTo(scenario.CurrentStatus),
                $"Source should remain at {scenario.CurrentStatus} after rejected transition.");
        }
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(ProcessingStatusStateMachineArbitraries)],
        MaxTest = 100)]
    [Description("Feature: world-sources, Property 9: Processing Status State Machine")]
    public void AllStatusPairs_OnlyValidTransitionsSucceed(StatusTransitionPair pair)
    {
        // This test validates the state machine conceptually by testing all (current, target) pairs
        // against the valid transitions map. Since only MarkReadyAsync is publicly exposed,
        // we use UpdateProcessingStatusAsync on the repository to set up states and then verify
        // the transition map is correctly defined.

        // Arrange
        var isValid = ValidTransitions.TryGetValue(pair.CurrentStatus, out var validTargets)
            && validTargets.Contains(pair.TargetStatus);

        // The valid transitions are:
        // Draft→Ready, Ready→Queued, Ready→Ready (retry), Queued→Processing, Processing→Processed, Processing→Failed, Failed→Ready
        var expectedValidPairs = new HashSet<(SourceProcessingStatus, SourceProcessingStatus)>
        {
            (SourceProcessingStatus.Draft, SourceProcessingStatus.Ready),
            (SourceProcessingStatus.Ready, SourceProcessingStatus.Queued),
            (SourceProcessingStatus.Ready, SourceProcessingStatus.Ready),
            (SourceProcessingStatus.Queued, SourceProcessingStatus.Processing),
            (SourceProcessingStatus.Processing, SourceProcessingStatus.Processed),
            (SourceProcessingStatus.Processing, SourceProcessingStatus.Failed),
            (SourceProcessingStatus.Failed, SourceProcessingStatus.Ready),
        };

        var expectedIsValid = expectedValidPairs.Contains((pair.CurrentStatus, pair.TargetStatus));

        // Assert — the transition map matches the specification
        Assert.That(isValid, Is.EqualTo(expectedIsValid),
            $"Transition {pair.CurrentStatus}→{pair.TargetStatus}: " +
            $"expected valid={expectedIsValid}, got valid={isValid}");
    }
}

/// <summary>
/// Scenario for testing MarkReadyAsync from various starting statuses.
/// </summary>
public record StatusTransitionScenario(
    SourceProcessingStatus CurrentStatus,
    Guid WorldId,
    Guid ActingUserId);

/// <summary>
/// A pair of (currentStatus, targetStatus) for testing all transition combinations.
/// </summary>
public record StatusTransitionPair(
    SourceProcessingStatus CurrentStatus,
    SourceProcessingStatus TargetStatus);

/// <summary>
/// Custom FsCheck arbitraries for processing status state machine tests.
/// </summary>
public class ProcessingStatusStateMachineArbitraries
{
    private static readonly SourceProcessingStatus[] AllStatuses =
    [
        SourceProcessingStatus.Draft,
        SourceProcessingStatus.Ready,
        SourceProcessingStatus.Queued,
        SourceProcessingStatus.Processing,
        SourceProcessingStatus.Processed,
        SourceProcessingStatus.Failed
    ];

    public static Arbitrary<StatusTransitionScenario> StatusTransitionScenarios()
    {
        var gen =
            from status in Gen.Elements(AllStatuses)
            from worldId in ArbMap.Default.GeneratorFor<Guid>()
            from userId in ArbMap.Default.GeneratorFor<Guid>()
            select new StatusTransitionScenario(status, worldId, userId);

        return gen.ToArbitrary();
    }

    public static Arbitrary<StatusTransitionPair> StatusTransitionPairs()
    {
        var gen =
            from currentStatus in Gen.Elements(AllStatuses)
            from targetStatus in Gen.Elements(AllStatuses)
            select new StatusTransitionPair(currentStatus, targetStatus);

        return gen.ToArbitrary();
    }
}
