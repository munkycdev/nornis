using System.Reflection;
using Nornis.Domain.Repositories;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Repositories;

[TestFixture]
public class RepositoryInterfaceContractTests
{
    /// <summary>
    /// Every repository interface in the namespace, discovered rather than listed. The list
    /// used to be written out by hand, which meant a new interface was covered by the
    /// convention sweeps below only if someone remembered to add it — and a second test
    /// existed purely to count the list against the assembly and catch that. Reading the
    /// assembly removes both the staleness and the test guarding against it.
    /// </summary>
    private static readonly Type[] RepositoryInterfaces =
        [.. typeof(IWorldRepository).Assembly
            .GetTypes()
            .Where(t => t.IsInterface && t.Namespace == "Nornis.Domain.Repositories")
            .OrderBy(t => t.Name)];

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
}
