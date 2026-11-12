using Nornis.Application.Ai;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Ai;

/// <summary>
/// Every metered AI operation takes its model choice from exactly one task (feature 39). A new
/// <see cref="AiOperationType"/> fails here until <see cref="AiTasks.For"/> places it, rather than
/// running on whatever model a default arm would have picked.
/// </summary>
[TestFixture]
public class AiTasksTests
{
    [TestCaseSource(nameof(AllOperations))]
    public void EveryOperation_IsPlaced(AiOperationType operation)
    {
        Assert.DoesNotThrow(() => AiTasks.For(operation));
    }

    [Test]
    public void OnlyEmbedding_HasNoTask()
    {
        // Embeddings are Nornis's to pay for (and go with phase H); nobody picks their model.
        var withoutTask = AllOperations().Where(o => AiTasks.For(o) is null);

        Assert.That(withoutTask, Is.EqualTo([AiOperationType.Embedding]));
    }

    [TestCase(AiOperationType.AskLoremaster, AiTask.Ask)]
    [TestCase(AiOperationType.SourceExtraction, AiTask.Extraction)]
    [TestCase(AiOperationType.MapExtraction, AiTask.Extraction)]
    [TestCase(AiOperationType.AdventureDrafting, AiTask.Prep)]
    [TestCase(AiOperationType.AdventureConversation, AiTask.Prep)]
    [TestCase(AiOperationType.CoverArtBrief, AiTask.Prep)]
    [TestCase(AiOperationType.CoverArt, AiTask.CoverArt)]
    [TestCase(AiOperationType.KeyArt, AiTask.CoverArt)]
    [TestCase(AiOperationType.CoverArtRefinement, AiTask.ArtRefinement)]
    [TestCase(AiOperationType.KeyArtRefinement, AiTask.ArtRefinement)]
    [TestCase(AiOperationType.WorldNaming, AiTask.Chores)]
    public void Operation_BelongsToItsTask(AiOperationType operation, AiTask task)
    {
        Assert.That(AiTasks.For(operation), Is.EqualTo(task));
    }

    [Test]
    public void OnlyTheImageTasks_AreKeysOnly()
    {
        var keysOnly = Enum.GetValues<AiTask>().Where(AiTasks.OnlyKeysRun);

        Assert.That(keysOnly, Is.EqualTo([AiTask.CoverArt, AiTask.ArtRefinement]),
            "an image task has no Nornis deployment, so neither Nornis, the trial nor a compatible line runs it");
    }

    private static IEnumerable<AiOperationType> AllOperations() => Enum.GetValues<AiOperationType>();
}
