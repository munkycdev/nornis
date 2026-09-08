using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

[TestFixture]
public class MigrationStateMemoTests
{
    [Test]
    public void StartsNotUpToDate()
    {
        // The memo must begin by trusting nothing: a fresh process has not yet seen the
        // schema, and the first /health after a deploy is the one the rollout waits on.
        Assert.That(new MigrationStateMemo().UpToDate, Is.False);
    }

    [Test]
    public void MarkUpToDate_IsSticky()
    {
        var memo = new MigrationStateMemo();

        memo.MarkUpToDate();

        Assert.That(memo.UpToDate, Is.True);
    }
}
