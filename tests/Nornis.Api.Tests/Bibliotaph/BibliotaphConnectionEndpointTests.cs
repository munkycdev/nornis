using System.Net;
using System.Net.Http.Json;
using Nornis.Api.Tests.Infrastructure;
using NUnit.Framework;

namespace Nornis.Api.Tests.Bibliotaph;

/// <summary>The connection routes with the integration unconfigured, as production is until Dave sets it up.</summary>
[TestFixture]
public class BibliotaphConnectionEndpointTests
{
    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new NornisWebApplicationFactory();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    private sealed record ConnectionView(bool Configured, string Status);

    [Test]
    public async Task Unconfigured_the_profile_section_is_hidden()
    {
        var view = await _factory.CreateAuthenticatedClient().GetFromJsonAsync<ConnectionView>("/api/bibliotaph/connection");

        Assert.That(view!.Configured, Is.False);
        Assert.That(view.Status, Is.EqualTo("none"));
    }

    [Test]
    public async Task Unconfigured_begin_is_a_404()
    {
        var response = await _factory.CreateAuthenticatedClient().PostAsync("/api/bibliotaph/connection/begin", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    [Category("Authorization")]
    public async Task The_connection_needs_a_signed_in_user()
    {
        var response = await _factory.CreateClient().GetAsync("/api/bibliotaph/connection");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    [Category("Authorization")]
    public async Task The_callback_is_anonymous_and_only_redirects()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/bibliotaph/callback?code=x&state=forged");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
        Assert.That(response.Headers.Contains("Set-Cookie"), Is.False);
    }

    [Test]
    public async Task Disconnecting_with_no_connection_is_fine()
    {
        var response = await _factory.CreateAuthenticatedClient().DeleteAsync("/api/bibliotaph/connection");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }
}
