using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Nornis.Application.Authorization;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 8: Role Hierarchy Enforcement
///
/// For any pair of WorldRoles (actualRole, requiredRole), access to an operation
/// requiring requiredRole should be granted if and only if actualRole.Rank >= requiredRole.Rank,
/// using the hierarchy GM (3) > Player (2) > Observer (1).
///
/// **Validates: Requirements 8.3, 8.4**
/// </summary>
[TestFixture]
public class RoleHierarchyEnforcementTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(RoleHierarchyArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-worlds, Property 8: Role Hierarchy Enforcement")]
    public void IsAtLeast_GrantsAccess_IfAndOnlyIf_ActualRankIsGreaterThanOrEqualToRequired(RolePair pair)
    {
        // Act
        var accessGranted = pair.ActualRole.IsAtLeast(pair.RequiredRole);

        // Assert — access should be granted iff actualRole.Rank() >= requiredRole.Rank()
        var expectedAccess = pair.ActualRole.Rank() >= pair.RequiredRole.Rank();

        Assert.That(accessGranted, Is.EqualTo(expectedAccess),
            $"For actualRole={pair.ActualRole} (rank {pair.ActualRole.Rank()}) and " +
            $"requiredRole={pair.RequiredRole} (rank {pair.RequiredRole.Rank()}), " +
            $"expected access={expectedAccess} but got access={accessGranted}");
    }
}

/// <summary>
/// Input model representing a pair of (actualRole, requiredRole) for hierarchy testing.
/// </summary>
public record RolePair(WorldRole ActualRole, WorldRole RequiredRole);

/// <summary>
/// Custom FsCheck arbitraries for Role Hierarchy Enforcement tests.
/// Generates all valid pairs of WorldRole enum values.
/// </summary>
public class RoleHierarchyArbitraries
{
    public static Arbitrary<RolePair> RolePairs()
    {
        var gen =
            from actualRole in Gen.Elements(WorldRole.GM, WorldRole.Player, WorldRole.Observer)
            from requiredRole in Gen.Elements(WorldRole.GM, WorldRole.Player, WorldRole.Observer)
            select new RolePair(actualRole, requiredRole);

        return gen.ToArbitrary();
    }
}
