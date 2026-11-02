using Nornis.Application.Accounts;
using NUnit.Framework;

namespace Nornis.Application.Tests.Accounts;

[TestFixture]
public class SignInProvidersTests
{
    [TestCase("oauth2|discord|992501439685460139", "Discord")]
    [TestCase("discord|992501439685460139", "Discord")]
    [TestCase("google-oauth2|109853919976332151602", "Google")]
    [TestCase("windowslive|c512f408462e814a", "Microsoft")]
    [TestCase("apple|001234.abcd", "Apple")]
    [TestCase("github|1234", "GitHub")]
    [TestCase("auth0|6543", "Email")]
    [TestCase("oauth2|patreon|77", "patreon")]
    [TestCase("twitch|77", "twitch")]
    public void A_subject_names_its_provider(string subject, string provider) =>
        Assert.That(SignInProviders.Name(subject), Is.EqualTo(provider));
}
