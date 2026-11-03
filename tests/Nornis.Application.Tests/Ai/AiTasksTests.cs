using Nornis.Application.Ai;
using Nornis.Application.Services;
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
    [TestCase(AiOperationType.WorldNaming, AiTask.Chores)]
    public void Operation_BelongsToItsTask(AiOperationType operation, AiTask task)
    {
        Assert.That(AiTasks.For(operation), Is.EqualTo(task));
    }

    [Test]
    public async Task NornisFunding_RunsEveryCallOnNornisModel()
    {
        // Phase A: the seam exists and changes nothing; Azure still pays for every call.
        var funding = await new NornisAiFundingService().ResolveAsync(
            AiCallScope.ForWorld(Guid.NewGuid()), AiTask.Ask, "nornis-ask", CancellationToken.None);

        Assert.That(funding, Is.EqualTo(new AiFunding(AiProvider.AzureOpenAi, "nornis-ask", AiFundedBy.Nornis)));
    }

    private static IEnumerable<AiOperationType> AllOperations() => Enum.GetValues<AiOperationType>();
}
