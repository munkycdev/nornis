using Nornis.Application.Prep;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Prep;

/// <summary>A research answer's links to its own listed pages become their text (feature 40, Requirement 3.3).</summary>
[TestFixture]
public class WebPageLinksTests
{
    private static readonly IReadOnlyList<WebPage> Pages =
    [
        new("Wards", "https://example.org/wards?utm_source=openai"),
        new("Castle", "https://example.org/castle/"),
    ];

    [TestCase("See [the wards](https://example.org/wards?utm_source=openai).", "See the wards.")]
    [TestCase("See [the wards](https://example.org/wards).", "See the wards.", TestName = "without the tracking parameter")]
    [TestCase("See [the castle](https://example.org/castle).", "See the castle.", TestName = "without the trailing slash")]
    [TestCase("See [the castle](<https://example.org/castle/> \"Castle Ward\").", "See the castle.", TestName = "angle brackets and a title")]
    [TestCase("([example.org](https://example.org/wards?utm_source=openai))", "(example.org)", TestName = "OpenAI's parenthesised citation")]
    public void ALinkToAListedPage_KeepsItsTextOnly(string text, string expected) =>
        Assert.That(WebPageLinks.Strip(text, Pages), Is.EqualTo(expected));

    [TestCase("See [a map](https://maps.example.net/wards).")]
    [TestCase("See [the wards](https://example.org/wards?page=2).", TestName = "a query that isn't tracking is a different page")]
    [TestCase("Bracketed [text] (not a link) and ![an image](https://example.org/wards).", TestName = "an image keeps its link")]
    [TestCase("No links at all.")]
    public void AnythingElse_IsLeftAsWritten(string text) =>
        Assert.That(WebPageLinks.Strip(text, Pages), Is.EqualTo(text));

    [Test]
    public void WithNoPages_TheTextIsUnchanged() =>
        Assert.That(WebPageLinks.Strip("[the wards](https://example.org/wards)", []), Is.EqualTo("[the wards](https://example.org/wards)"));
}
