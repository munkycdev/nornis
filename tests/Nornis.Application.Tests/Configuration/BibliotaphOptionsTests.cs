using Nornis.Application.Configuration;
using NUnit.Framework;

namespace Nornis.Application.Tests.Configuration;

[TestFixture]
public class BibliotaphOptionsTests
{
    [Test]
    public void The_defaults_are_the_bounds_the_design_names()
    {
        var o = new BibliotaphOptions();

        Assert.Multiple(() =>
        {
            Assert.That(o.PassagesTimeoutSeconds, Is.EqualTo(4));
            Assert.That(o.BooksTimeoutSeconds, Is.EqualTo(5));
            Assert.That(o.PagesTimeoutSeconds, Is.EqualTo(8));
            Assert.That(o.TicketTimeoutSeconds, Is.EqualTo(5));
            Assert.That(o.CoverTimeoutSeconds, Is.EqualTo(5));
            Assert.That(o.MaxCoverBytes, Is.EqualTo(2 * 1024 * 1024));
            Assert.That(o.WarmUpTimeoutSeconds, Is.EqualTo(2));
            Assert.That(o.WebhookTimeoutSeconds, Is.EqualTo(15));
            Assert.That(o.ReconcileThrottleSeconds, Is.EqualTo(60));
            Assert.That(o.WarmUpThrottleMinutes, Is.EqualTo(10));
            Assert.That(o.OutboxBatchSize, Is.EqualTo(100));
            Assert.That(o.OutboxMaxAgeDays, Is.EqualTo(7));
            Assert.That(o.OutboxRetentionDays, Is.EqualTo(7));
            Assert.That(o.ReadingRecordRetentionDays, Is.EqualTo(90));
            Assert.That(o.MaxExcerptPages, Is.EqualTo(12));
            Assert.That(o.StartupProblem(), Is.Null);
        });
    }

    [Test]
    public void Unconfigured_by_default_so_production_is_unchanged_until_the_secrets_are_set()
    {
        Assert.That(new BibliotaphOptions().IsConfigured, Is.False);
    }

    [Test]
    public void Configured_needs_all_four_identity_settings()
    {
        var o = Configured();
        Assert.That(o.IsConfigured, Is.True);

        o.ClientSecret = " ";
        Assert.That(o.IsConfigured, Is.False);
    }

    [Test]
    public void A_zero_bound_refuses_to_start()
    {
        var o = new BibliotaphOptions { PassagesTimeoutSeconds = 0 };

        Assert.That(o.FirstNonPositiveBound(), Is.EqualTo(nameof(BibliotaphOptions.PassagesTimeoutSeconds)));
        Assert.That(o.StartupProblem(), Does.Contain("PassagesTimeoutSeconds"));
    }

    [Test]
    public void A_batch_over_the_webhook_limit_refuses_to_start()
    {
        var o = new BibliotaphOptions { OutboxBatchSize = BibliotaphOptions.MaxEventsPerCall + 1 };

        Assert.That(o.StartupProblem(), Does.Contain("OutboxBatchSize"));
    }

    [Test]
    public void Configured_without_a_key_ring_refuses_to_start()
    {
        var o = Configured();
        o.KeyBlobUri = null;

        Assert.That(o.StartupProblem(), Does.Contain("KeyBlobUri"));
    }

    private static BibliotaphOptions Configured() => new()
    {
        BaseUrl = "https://bibliotaph.example",
        Auth0Domain = "auth.example",
        ClientId = "nornis-to-bibliotaph",
        ClientSecret = "secret",
        KeyBlobUri = "https://storage.example/bibliotaph-keys/keys.xml",
    };
}
