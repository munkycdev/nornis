using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Nornis.Api.Filters;
using NUnit.Framework;

namespace Nornis.Api.Tests.Authorization;

/// <summary>
/// Every world-scoped controller must carry <see cref="WorldMemberActionFilter"/>.
///
/// This became load-bearing on 2026-08-02, when the world services stopped re-reading the
/// membership row and started trusting the role the filter resolved. Before that, a controller
/// missing the filter was a performance detail — the service would have re-checked anyway. Now
/// it is a hole: the action would run with whatever role the controller passed, for a caller
/// whose membership nobody verified.
///
/// The filter is opt-in per controller (`[ServiceFilter]`), so nothing but this test stands
/// between "someone adds a controller" and that hole.
/// </summary>
[TestFixture]
public class WorldMemberFilterCoverageTests
{
    private static IEnumerable<Type> WorldScopedControllers() =>
        typeof(Program).Assembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => t.GetCustomAttributes<RouteAttribute>()
                .Any(r => r.Template.Contains("{worldId", StringComparison.Ordinal)))
            .OrderBy(t => t.Name);

    [Test]
    [Category("Authorization")]
    public void EveryWorldScopedController_CarriesTheMembershipFilter()
    {
        var missing = WorldScopedControllers()
            .Where(t => !t.GetCustomAttributes<ServiceFilterAttribute>()
                .Any(f => f.ServiceType == typeof(WorldMemberActionFilter)))
            .Select(t => t.Name)
            .ToList();

        Assert.That(missing, Is.Empty,
            "These controllers route on {worldId} but never resolve the caller's membership. "
            + "The services behind them trust the role the filter puts in HttpContext, so without "
            + "it the action runs for a caller nobody checked.");
    }

    [Test]
    public void TheFilterCoverageTest_IsLookingAtSomething()
    {
        // A reflection sweep that silently matches nothing passes forever. If the routing
        // convention changes, this fails rather than the check above quietly going hollow.
        Assert.That(WorldScopedControllers().Count(), Is.GreaterThan(10));
    }
}
