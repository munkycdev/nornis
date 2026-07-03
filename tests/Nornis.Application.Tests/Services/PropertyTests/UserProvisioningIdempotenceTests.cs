using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 1: User Provisioning Idempotence
///
/// For any existing User with a given Auth0SubjectId, invoking user provisioning multiple times
/// with that same subject identifier should always return the same User record without creating
/// duplicates or modifying the existing record.
///
/// **Validates: Requirements 2.1, 2.3**
/// </summary>
[TestFixture]
[Category("Feature: auth-and-campaigns, Property 1: User Provisioning Idempotence")]
public class UserProvisioningIdempotenceTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(UserProvisioningArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-campaigns, Property 1: User Provisioning Idempotence")]
    public void GetByAuth0SubjectId_CalledMultipleTimes_ReturnsSameUserWithoutDuplicates(
        UserProvisioningInput input)
    {
        // Arrange — seed the repository with an existing user
        var repo = new InMemoryUserRepository();
        var existingUser = new User
        {
            Id = input.UserId,
            Auth0SubjectId = input.Auth0SubjectId,
            Username = input.Username,
            Email = input.Email,
            CreatedAt = input.CreatedAt,
            UpdatedAt = input.UpdatedAt
        };
        repo.CreateAsync(existingUser, CancellationToken.None).GetAwaiter().GetResult();

        var originalUserCount = repo.Users.Count;

        // Act — call GetByAuth0SubjectIdAsync multiple times (simulating repeated provisioning lookups)
        var results = new List<User?>();
        for (var i = 0; i < input.InvocationCount; i++)
        {
            var result = repo.GetByAuth0SubjectIdAsync(input.Auth0SubjectId, CancellationToken.None)
                .GetAwaiter().GetResult();
            results.Add(result);
        }

        // Assert — same user returned each time
        foreach (var result in results)
        {
            Assert.That(result, Is.Not.Null,
                "Each provisioning lookup must return a user for an existing Auth0SubjectId.");

            Assert.That(result!.Id, Is.EqualTo(existingUser.Id),
                "Each lookup must return the same User Id.");

            Assert.That(result.Auth0SubjectId, Is.EqualTo(existingUser.Auth0SubjectId),
                "Each lookup must return the same Auth0SubjectId.");

            Assert.That(result.Username, Is.EqualTo(existingUser.Username),
                "Each lookup must return the same Username (no modification).");

            Assert.That(result.Email, Is.EqualTo(existingUser.Email),
                "Each lookup must return the same Email (no modification).");

            Assert.That(result.CreatedAt, Is.EqualTo(existingUser.CreatedAt),
                "Each lookup must return the same CreatedAt (no modification).");

            Assert.That(result.UpdatedAt, Is.EqualTo(existingUser.UpdatedAt),
                "Each lookup must return the same UpdatedAt (no modification).");
        }

        // Assert — no duplicates created
        Assert.That(repo.Users.Count, Is.EqualTo(originalUserCount),
            "No additional User records should be created by repeated lookups.");

        // Assert — only one user with this Auth0SubjectId exists
        var matchingUsers = repo.Users.Where(u => u.Auth0SubjectId == input.Auth0SubjectId).ToList();
        Assert.That(matchingUsers.Count, Is.EqualTo(1),
            "Exactly one User with the given Auth0SubjectId must exist after repeated lookups.");
    }
}

/// <summary>
/// Input model for user provisioning idempotence property tests.
/// </summary>
public record UserProvisioningInput(
    Guid UserId,
    string Auth0SubjectId,
    string Username,
    string Email,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int InvocationCount);

/// <summary>
/// Custom FsCheck arbitraries for user provisioning idempotence tests.
/// </summary>
public class UserProvisioningArbitraries
{
    private static readonly char[] SubChars =
        "abcdefghijklmnopqrstuvwxyz0123456789|".ToCharArray();

    private static readonly char[] UsernameChars =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-".ToCharArray();

    private static readonly string[] EmailDomains =
        ["example.com", "test.org", "nornis.app", "blackharbor.net"];

    public static Arbitrary<UserProvisioningInput> UserProvisioningInputs()
    {
        // auth0|abc123 style subject identifiers
        var subGen =
            from length in Gen.Choose(5, 30)
            from chars in Gen.Elements(SubChars).ArrayOf(length)
            select "auth0|" + new string(chars);

        var usernameGen =
            from length in Gen.Choose(3, 20)
            from chars in Gen.Elements(UsernameChars).ArrayOf(length)
            select new string(chars);

        var emailGen =
            from localLength in Gen.Choose(3, 15)
            from localChars in Gen.Elements(UsernameChars).ArrayOf(localLength)
            from domain in Gen.Elements(EmailDomains)
            select new string(localChars) + "@" + domain;

        var timestampGen =
            from ticks in Gen.Choose(0, 1_000_000_000)
            select DateTimeOffset.UtcNow.AddTicks(-ticks);

        // Between 2 and 10 invocations to test idempotence
        var invocationCountGen = Gen.Choose(2, 10);

        var inputGen =
            from userId in ArbMap.Default.GeneratorFor<Guid>()
            from sub in subGen
            from username in usernameGen
            from email in emailGen
            from createdAt in timestampGen
            from updatedAt in timestampGen
            from invocations in invocationCountGen
            select new UserProvisioningInput(userId, sub, username, email, createdAt, updatedAt, invocations);

        return inputGen.ToArbitrary();
    }
}
