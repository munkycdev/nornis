using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 2: Invalid Titles Are Rejected
///
/// For any string that is null, empty, composed entirely of whitespace, or longer than 200 characters,
/// both source creation and source update operations should reject the input with a validation error
/// without modifying any stored data.
///
/// **Validates: Requirements 1.5, 3.4**
/// </summary>
[TestFixture]
[Category("Feature: campaign-sources, Property 2: Invalid Titles Are Rejected")]
public class SourceInvalidTitleRejectionTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(InvalidSourceTitleArbitraries)],
        MaxTest = 100)]
    [Description("Feature: campaign-sources, Property 2: Invalid Titles Are Rejected - CreateAsync rejects invalid titles")]
    public void CreateAsync_RejectsInvalidTitles(InvalidSourceTitleInput input)
    {
        // Arrange
        var sourceRepo = new InMemorySourceRepository();
        var memberRepo = new InMemoryCampaignMemberRepository();
        var queueClient = new FakeExtractionQueueClient();
        var service = new SourceService(sourceRepo, memberRepo, queueClient);

        var command = new CreateSourceCommand(
            input.CampaignId,
            input.InvalidTitle!,
            SourceType.SessionNote,
            VisibilityScope.PartyVisible,
            input.UserId,
            CampaignRole.GM,
            Body: "Session 4 — Questioning Captain Voss in Black Harbor");

        // Act
        var result = service.CreateAsync(command, CancellationToken.None).GetAwaiter().GetResult();

        // Assert - operation should fail with validation error
        Assert.That(result.IsSuccess, Is.False,
            $"Source creation should reject invalid title: \"{input.InvalidTitle ?? "(null)"}\"");
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error!.Code, Is.EqualTo("validation_error"),
            "Error code should be validation_error.");
        Assert.That(result.Error.StatusCode, Is.EqualTo(400),
            "Validation error should return status code 400.");

        // Assert - no data was stored
        Assert.That(sourceRepo.Sources, Is.Empty,
            "No source should be stored when title is invalid.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(InvalidSourceTitleArbitraries)],
        MaxTest = 100)]
    [Description("Feature: campaign-sources, Property 2: Invalid Titles Are Rejected - UpdateAsync rejects invalid titles")]
    public void UpdateAsync_RejectsInvalidTitles(InvalidSourceTitleInput input)
    {
        // Arrange - create a valid source first
        var sourceRepo = new InMemorySourceRepository();
        var memberRepo = new InMemoryCampaignMemberRepository();
        var queueClient = new FakeExtractionQueueClient();
        var service = new SourceService(sourceRepo, memberRepo, queueClient);

        var validSource = new Source
        {
            Id = Guid.NewGuid(),
            CampaignId = input.CampaignId,
            Type = SourceType.SessionNote,
            Title = "Tavrin's Journal — The Silver Key",
            Body = "Found a silver key in Captain Voss's quarters.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Draft,
            CreatedByUserId = input.UserId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        sourceRepo.CreateAsync(validSource, CancellationToken.None).GetAwaiter().GetResult();

        var originalTitle = validSource.Title;
        var originalBody = validSource.Body;

        // Act - attempt update with invalid title
        var updateCommand = new UpdateSourceCommand(
            validSource.Id,
            input.CampaignId,
            input.UserId,
            CampaignRole.GM,
            Title: input.InvalidTitle);

        var result = service.UpdateAsync(updateCommand, CancellationToken.None).GetAwaiter().GetResult();

        // Assert - operation should fail with validation error
        Assert.That(result.IsSuccess, Is.False,
            $"Source update should reject invalid title: \"{input.InvalidTitle ?? "(null)"}\"");
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error!.Code, Is.EqualTo("validation_error"),
            "Error code should be validation_error.");
        Assert.That(result.Error.StatusCode, Is.EqualTo(400),
            "Validation error should return status code 400.");

        // Assert - source was not modified
        var storedSource = sourceRepo.Sources.Single(s => s.Id == validSource.Id);
        Assert.That(storedSource.Title, Is.EqualTo(originalTitle),
            "Source title should not be modified when update is rejected.");
        Assert.That(storedSource.Body, Is.EqualTo(originalBody),
            "Source body should not be modified when update is rejected.");
    }
}

/// <summary>
/// Input model for invalid source title property tests.
/// </summary>
public record InvalidSourceTitleInput(
    string? InvalidTitle,
    Guid CampaignId,
    Guid UserId);

/// <summary>
/// Custom FsCheck arbitraries for invalid source title generation.
/// Generates: empty strings, whitespace-only strings, and strings longer than 200 characters.
/// </summary>
public class InvalidSourceTitleArbitraries
{
    private static readonly char[] WhitespaceChars = [' ', '\t', '\n', '\r'];

    private static readonly char[] AlphanumericChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

    public static Arbitrary<InvalidSourceTitleInput> InvalidSourceTitleInputs()
    {
        // Generator for empty string
        var emptyGen = Gen.Constant("");

        // Generator for whitespace-only strings (1-50 whitespace characters)
        var whitespaceOnlyGen =
            from length in Gen.Choose(1, 50)
            from chars in Gen.Elements(WhitespaceChars).ArrayOf(length)
            select new string(chars);

        // Generator for strings longer than 200 characters (201-500 chars)
        var tooLongGen =
            from length in Gen.Choose(201, 500)
            from chars in Gen.Elements(AlphanumericChars).ArrayOf(length)
            select new string(chars);

        // Combine all invalid title generators with equal probability
        var invalidTitleGen = Gen.OneOf(emptyGen, whitespaceOnlyGen, tooLongGen);

        var inputGen =
            from title in invalidTitleGen
            from campaignId in ArbMap.Default.GeneratorFor<Guid>()
            from userId in ArbMap.Default.GeneratorFor<Guid>()
            select new InvalidSourceTitleInput(title, campaignId, userId);

        return inputGen.ToArbitrary();
    }
}
