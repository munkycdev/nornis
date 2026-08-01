using Nornis.Api.Extensions;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Api.Tests.Extensions;

/// <summary>
/// EnumParsing.TryParseDefined is the one parse behind every enum-carrying request
/// string, so its contract is pinned once here instead of per controller.
/// </summary>
[TestFixture]
public class EnumParsingTests
{
    [TestCase("Character")]
    [TestCase("character")]
    [TestCase("CHARACTER")]
    public void ParsesNamesCaseInsensitively(string value)
    {
        Assert.That(EnumParsing.TryParseDefined<ArtifactType>(value, out var parsed), Is.True);
        Assert.That(parsed, Is.EqualTo(ArtifactType.Character));
    }

    [TestCase("7")]
    [TestCase("-1")]
    [TestCase("999")]
    public void RejectsBareNumerals(string value)
    {
        // Enum.TryParse alone accepts these and yields undefined values that match
        // nothing downstream — the empty-canon bug. They must read as 400s.
        Assert.That(EnumParsing.TryParseDefined<ArtifactType>(value, out _), Is.False);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("NotAThing")]
    public void RejectsUnknownNames(string value)
    {
        Assert.That(EnumParsing.TryParseDefined<ArtifactType>(value, out _), Is.False);
    }
}
