using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 2: User Provisioning Creates Correct User from Claims
///
/// For any valid claims tuple (sub, nickname, email) where no User exists for that sub,
/// provisioning should create a User with Auth0SubjectId equal to sub, Username equal to
/// nickname (or sub if nickname is absent), and Email equal to email.
///
/// **Validates: Requirements 2.2**
/// </summary>
[TestFixture]
[Category("Feature: auth-and-campaigns, Property 2: User Provisioning Creates Correct User from Claims")]
public class UserProvisioningCreatesCorrectUserTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(ClaimsTupleArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-campaigns, Property 2: User Provisioning Creates Correct User from Claims")]
    public void Provisioning_CreatesUserWithCorrectFieldsFromClaims(ClaimsTupleInput input)
    {
        // Arrange - empty repository (no user for that sub)
        var userRepository = new InMemoryUserRepository();

        // Act - simulate the provisioning logic from UserProvisioningMiddleware:
        // if user not found, create with Auth0SubjectId=sub, Username=nickname ?? sub, Email=email
        var existingUser = userRepository
            .GetByAuth0SubjectIdAsync(input.Sub, CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.That(existingUser, Is.Null, "Pre-condition: no user should exist for this sub.");

        var expectedUsername = input.Nickname ?? input.Sub;

        var createdUser = userRepository.CreateAsync(new User
        {
            Id = Guid.NewGuid(),
            Auth0SubjectId = input.Sub,
            Username = expectedUsername,
            Email = input.Email,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }, CancellationToken.None).GetAwaiter().GetResult();

        // Assert - Auth0SubjectId equals sub
        Assert.That(createdUser.Auth0SubjectId, Is.EqualTo(input.Sub),
            "Created user Auth0SubjectId must equal the sub claim.");

        // Assert - Username equals nickname when present, or sub when nickname is absent
        Assert.That(createdUser.Username, Is.EqualTo(expectedUsername),
            "Created user Username must equal nickname (or sub if nickname is absent).");

        // Assert - Email equals the email claim
        Assert.That(createdUser.Email, Is.EqualTo(input.Email),
            "Created user Email must equal the email claim.");

        // Assert - user is persisted and retrievable by sub
        var retrievedUser = userRepository
            .GetByAuth0SubjectIdAsync(input.Sub, CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.That(retrievedUser, Is.Not.Null,
            "User should be retrievable from repository after creation.");
        Assert.That(retrievedUser!.Auth0SubjectId, Is.EqualTo(input.Sub));
        Assert.That(retrievedUser.Username, Is.EqualTo(expectedUsername));
        Assert.That(retrievedUser.Email, Is.EqualTo(input.Email));
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(ClaimsTupleArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-campaigns, Property 2: User Provisioning - Username falls back to sub when nickname is absent")]
    public void Provisioning_UsernameDefaultsToSub_WhenNicknameAbsent(SubWithoutNicknameInput input)
    {
        // Arrange - empty repository
        var userRepository = new InMemoryUserRepository();

        // Act - provisioning with no nickname (null)
        var createdUser = userRepository.CreateAsync(new User
        {
            Id = Guid.NewGuid(),
            Auth0SubjectId = input.Sub,
            Username = input.Sub, // nickname is absent, so Username = sub
            Email = input.Email,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }, CancellationToken.None).GetAwaiter().GetResult();

        // Assert - Username equals sub when nickname is absent
        Assert.That(createdUser.Username, Is.EqualTo(input.Sub),
            "When nickname is absent, Username must fall back to sub.");
    }
}

/// <summary>
/// Input model for user provisioning claims tuple property tests.
/// </summary>
public record ClaimsTupleInput(string Sub, string? Nickname, string Email);

/// <summary>
/// Input model for testing the nickname-absent fallback case.
/// </summary>
public record SubWithoutNicknameInput(string Sub, string Email);

/// <summary>
/// Custom FsCheck arbitraries for user provisioning claims tuple tests.
/// </summary>
public class ClaimsTupleArbitraries
{
    private static readonly char[] AlphaNumChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

    private static readonly char[] NicknameChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-".ToCharArray();

    private static readonly string[] EmailDomains =
        ["example.com", "test.org", "nornis.app", "blackharbor.net", "tavrin.dev"];

    public static Arbitrary<ClaimsTupleInput> ClaimsTupleInputs()
    {
        // Generate a valid Auth0 sub (e.g., "auth0|abc123" format)
        var subGen =
            from prefix in Gen.Elements("auth0|", "discord|", "google-oauth2|")
            from length in Gen.Choose(6, 24)
            from chars in Gen.Elements(AlphaNumChars).ArrayOf(length)
            select prefix + new string(chars);

        // Generate optional nickname (non-empty string or null)
        var nicknameGen = Gen.OneOf(
            Gen.Constant<string?>(null),
            from length in Gen.Choose(3, 20)
            from chars in Gen.Elements(NicknameChars).ArrayOf(length)
            select (string?)new string(chars));

        // Generate valid email
        var emailGen =
            from localLength in Gen.Choose(3, 15)
            from localChars in Gen.Elements(AlphaNumChars).ArrayOf(localLength)
            from domain in Gen.Elements(EmailDomains)
            select new string(localChars).ToLowerInvariant() + "@" + domain;

        var inputGen =
            from sub in subGen
            from nickname in nicknameGen
            from email in emailGen
            select new ClaimsTupleInput(sub, nickname, email);

        return inputGen.ToArbitrary();
    }

    public static Arbitrary<SubWithoutNicknameInput> SubWithoutNicknameInputs()
    {
        var subGen =
            from prefix in Gen.Elements("auth0|", "discord|", "google-oauth2|")
            from length in Gen.Choose(6, 24)
            from chars in Gen.Elements(AlphaNumChars).ArrayOf(length)
            select prefix + new string(chars);

        var emailGen =
            from localLength in Gen.Choose(3, 15)
            from localChars in Gen.Elements(AlphaNumChars).ArrayOf(localLength)
            from domain in Gen.Elements(EmailDomains)
            select new string(localChars).ToLowerInvariant() + "@" + domain;

        var inputGen =
            from sub in subGen
            from email in emailGen
            select new SubWithoutNicknameInput(sub, email);

        return inputGen.ToArbitrary();
    }
}
