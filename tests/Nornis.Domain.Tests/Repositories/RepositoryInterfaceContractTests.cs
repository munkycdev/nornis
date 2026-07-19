using System.Reflection;
using Nornis.Domain.Repositories;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Repositories;

[TestFixture]
public class RepositoryInterfaceContractTests
{
    private static readonly Type[] RepositoryInterfaces =
    [
        typeof(IWorldRepository),
        typeof(IWorldMemberRepository),
        typeof(IWorldInviteRepository),
        typeof(IUserRepository),
        typeof(ISourceRepository),
        typeof(IArtifactRepository),
        typeof(IArtifactFactRepository),
        typeof(IArtifactRelationshipRepository),
        typeof(IReviewBatchRepository),
        typeof(IReviewProposalRepository),
        typeof(ISourceReferenceRepository),
        typeof(IAiUsageRecordRepository),
        typeof(IHealthAssessmentRepository),
        typeof(ICampaignRepository),
        typeof(ICharacterRepository),
        typeof(ILibraryDocumentRepository),
        typeof(ILibraryChunkRepository),
        typeof(ISourceAttachmentRepository),
        typeof(IMapPlacemarkRepository),
        typeof(IUnitOfWork),
        typeof(ITransactionScope),
    ];

    private static readonly Dictionary<Type, string[]> ExpectedMethods = new()
    {
        [typeof(IWorldRepository)] = ["CreateAsync", "GetByIdAsync", "UpdateAsync", "ListByUserAsync", "GetByIdsAsync"],
        [typeof(IWorldMemberRepository)] = ["CreateAsync", "GetByWorldAndUserAsync", "ListByWorldAsync", "RemoveAsync", "ListByUserAsync"],
        [typeof(IWorldInviteRepository)] = ["CreateAsync", "GetByCodeAsync", "GetByIdAsync", "ListByWorldAsync", "UpdateAsync"],
        [typeof(IUserRepository)] = ["CreateAsync", "GetByIdAsync", "GetByAuth0SubjectIdAsync", "ListAsync", "UpdateAsync"],
        [typeof(ISourceRepository)] = ["CreateAsync", "GetByIdAsync", "ListByWorldAsync", "UpdateProcessingStatusAsync"],
        [typeof(IArtifactRepository)] = ["CreateAsync", "GetByIdAsync", "ListByWorldAsync", "UpdateAsync"],
        [typeof(IArtifactFactRepository)] = ["CreateAsync", "GetByIdAsync", "ListByArtifactAsync", "UpdateAsync"],
        [typeof(IArtifactRelationshipRepository)] = ["CreateAsync", "GetByIdAsync", "ListByArtifactAsync", "UpdateAsync"],
        [typeof(IReviewBatchRepository)] = ["CreateAsync", "GetByIdAsync", "ListByWorldAsync", "UpdateStatusAsync"],
        [typeof(IReviewProposalRepository)] = ["CreateAsync", "GetByIdAsync", "ListByReviewBatchAsync", "ListPendingByWorldAsync", "UpdateAsync"],
        [typeof(ISourceReferenceRepository)] = ["CreateAsync", "ListByTargetAsync"],
        [typeof(IAiUsageRecordRepository)] = ["CreateAsync", "QueryAsync", "AggregateAsync", "AggregateByOperationTypeAsync", "AggregateByModelAsync", "AggregateByUserAsync", "AggregateByWorldAsync"],
        [typeof(IHealthAssessmentRepository)] = ["CreateAsync", "GetLatestWithFindingsAsync", "GetLatestCreatedAtAsync", "GetFindingByIdAsync", "UpdateFindingAsync"],
        [typeof(ICampaignRepository)] = ["CreateAsync", "GetByIdAsync", "ListByWorldAsync", "UpdateAsync", "DeleteAsync"],
        [typeof(ICharacterRepository)] = ["CreateAsync", "GetByIdAsync", "ListByWorldAsync", "ListByCampaignAsync", "UpdateAsync", "DeleteAsync", "ReplaceCampaignAssignmentsAsync"],
        [typeof(ILibraryDocumentRepository)] = ["CreateAsync", "GetByIdAsync", "ListByWorldAsync", "AnyIndexedAsync", "UpdateAsync", "DeleteAsync"],
        [typeof(ILibraryChunkRepository)] = ["ReplaceForDocumentAsync", "DeleteForDocumentAsync", "SearchAsync"],
        [typeof(ISourceAttachmentRepository)] = ["CreateAsync", "GetByIdAsync", "ListBySourceAsync", "UpdateAsync", "DeleteAsync"],
        [typeof(IUnitOfWork)] = ["BeginTransactionAsync"],
        [typeof(ITransactionScope)] = ["CommitAsync", "RollbackAsync"],
    };

    private static IEnumerable<TestCaseData> AllRepositoryMethods()
    {
        foreach (var interfaceType in RepositoryInterfaces)
        {
            foreach (var method in interfaceType.GetMethods())
            {
                yield return new TestCaseData(interfaceType, method)
                    .SetName($"{interfaceType.Name}.{method.Name}_AcceptsCancellationToken");
            }
        }
    }

    private static IEnumerable<TestCaseData> AllRepositoryMethodsForReturnType()
    {
        foreach (var interfaceType in RepositoryInterfaces)
        {
            foreach (var method in interfaceType.GetMethods())
            {
                yield return new TestCaseData(interfaceType, method)
                    .SetName($"{interfaceType.Name}.{method.Name}_ReturnsTask");
            }
        }
    }

    private static IEnumerable<TestCaseData> AllExpectedMethodSignatures()
    {
        foreach (var (interfaceType, methodNames) in ExpectedMethods)
        {
            foreach (var methodName in methodNames)
            {
                yield return new TestCaseData(interfaceType, methodName)
                    .SetName($"{interfaceType.Name}_Defines_{methodName}");
            }
        }
    }

    [TestCaseSource(nameof(AllRepositoryMethods))]
    public void AllMethods_AcceptCancellationToken(Type interfaceType, MethodInfo method)
    {
        var parameters = method.GetParameters();
        var hasCancellationToken = parameters.Any(p => p.ParameterType == typeof(CancellationToken));

        Assert.That(hasCancellationToken, Is.True,
            $"{interfaceType.Name}.{method.Name} must accept a CancellationToken parameter");
    }

    [TestCaseSource(nameof(AllRepositoryMethodsForReturnType))]
    public void AllMethods_ReturnTaskOrTaskOfT(Type interfaceType, MethodInfo method)
    {
        var returnType = method.ReturnType;

        var isTask = returnType == typeof(Task)
            || (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>));

        Assert.That(isTask, Is.True,
            $"{interfaceType.Name}.{method.Name} must return Task or Task<T>, but returns {returnType.Name}");
    }

    [TestCaseSource(nameof(AllExpectedMethodSignatures))]
    public void Interface_DefinesExpectedMethod(Type interfaceType, string methodName)
    {
        var methods = interfaceType.GetMethods()
            .Where(m => m.Name == methodName)
            .ToList();

        Assert.That(methods, Is.Not.Empty,
            $"{interfaceType.Name} must define method '{methodName}'");
    }

    [Test]
    public void AllRepositoryInterfaces_ArePresent()
    {
        var assembly = typeof(IWorldRepository).Assembly;
        var repositoryInterfaces = assembly.GetTypes()
            .Where(t => t.IsInterface && t.Namespace == "Nornis.Domain.Repositories")
            .OrderBy(t => t.Name)
            .ToList();

        Assert.That(repositoryInterfaces, Has.Count.EqualTo(RepositoryInterfaces.Length),
            $"Expected {RepositoryInterfaces.Length} repository interfaces, found: {string.Join(", ", repositoryInterfaces.Select(t => t.Name))}");
    }
}
