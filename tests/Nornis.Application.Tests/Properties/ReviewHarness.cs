using Nornis.Application.Application;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Application.Validation;

namespace Nornis.Application.Tests.Properties;

/// <summary>
/// One ReviewService and the fakes behind it.
///
/// This replaces six copies of the same thirty-five lines across four files, returning seven
/// byte-identical record declarations under six different names — <c>TestContext</c>,
/// <c>TestContextWithFakeApplicator</c>, <c>EditTestContext</c>, <c>RealTestContext</c>,
/// <c>FakeTestContext</c>. The names implied a difference the fields did not have: the only
/// thing that actually varied was which validator and applicator went in, which is now the
/// choice of builder and nothing else.
/// </summary>
internal sealed record ReviewHarness(
    ReviewService Service,
    InMemoryReviewProposalRepository ProposalRepo,
    InMemoryReviewBatchRepository BatchRepo,
    InMemorySourceRepository SourceRepo,
    InMemoryArtifactRepository ArtifactRepo,
    InMemoryArtifactFactRepository ArtifactFactRepo,
    InMemoryArtifactRelationshipRepository ArtifactRelationshipRepo,
    InMemorySourceReferenceRepository SourceRefRepo)
{
    /// <summary>
    /// Validator and applicator are fakes: the subject is ReviewService's own behaviour —
    /// authorization, status transitions, batch rollup — not what a payload turns into.
    /// </summary>
    public static ReviewHarness WithFakeApplicator() =>
        Build(new FakeProposalValidator(), repos => new FakeProposalApplicator());

    /// <summary>
    /// The real validator and applicator, for the properties whose subject *is* what a payload
    /// turns into — visibility inheritance, entity creation, merge reassignment.
    /// </summary>
    public static ReviewHarness WithRealApplicator() =>
        Build(new ProposalValidator(), repos => new ProposalApplicator(
            repos.ArtifactRepo,
            repos.ArtifactFactRepo,
            repos.ArtifactRelationshipRepo,
            repos.SourceRefRepo,
            new InMemorySourceAttachmentRepository(),
            new InMemoryMapPlacemarkRepository(),
            new InMemoryWorldMemberRepository()));

    private static ReviewHarness Build(
        IProposalValidator validator,
        Func<ReviewHarness, IProposalApplicator> applicatorFor)
    {
        var batchRepo = new InMemoryReviewBatchRepository();
        var harness = new ReviewHarness(
            null!,
            new InMemoryReviewProposalRepository(batchRepo),
            batchRepo,
            new InMemorySourceRepository(),
            new InMemoryArtifactRepository(),
            new InMemoryArtifactFactRepository(),
            new InMemoryArtifactRelationshipRepository(),
            new InMemorySourceReferenceRepository());

        return harness with
        {
            Service = new ReviewService(
                harness.ProposalRepo,
                harness.BatchRepo,
                harness.SourceRepo,
                harness.ArtifactRepo,
                harness.ArtifactFactRepo,
                harness.ArtifactRelationshipRepo,
                harness.SourceRefRepo,
                new FakeUnitOfWork(),
                validator,
                applicatorFor(harness),
            replayAdvancer: NoOpExtractionReplayAdvancer.Instance)
        };
    }
}
