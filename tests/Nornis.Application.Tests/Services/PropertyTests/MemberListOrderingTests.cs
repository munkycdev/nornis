using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 10: Member List Ordering
///
/// For any campaign with N members (N ≥ 1), listing members should return exactly N entries
/// where each entry's JoinedAt is less than or equal to the next entry's JoinedAt (ascending order).
///
/// **Validates: Requirements 6.1, 6.4**
/// </summary>
[TestFixture]
public class MemberListOrderingTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(MemberListOrderingArbitraries)],
        MaxTest = 100)]
    [Description("Feature: auth-and-campaigns, Property 10: Member List Ordering")]
    public void MemberListIsOrderedByJoinedAtAscending(MemberListOrderingInput input)
    {
        // Arrange
        var memberRepo = new InMemoryCampaignMemberRepository();
        var userRepo = new InMemoryUserRepository();
        var service = new CampaignMemberService(memberRepo, userRepo);

        // Seed all members into the repository
        foreach (var member in input.Members)
        {
            memberRepo.CreateAsync(member, CancellationToken.None).GetAwaiter().GetResult();
        }

        // Act — list members as the requesting user (who is one of the members)
        var result = service.ListMembersAsync(
            input.CampaignId,
            input.RequestingUserId,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert — result is successful
        Assert.That(result.IsSuccess, Is.True, "ListMembersAsync should succeed for a campaign member");

        var members = result.Value!;

        // Assert — exactly N entries returned
        Assert.That(members.Count, Is.EqualTo(input.Members.Count),
            $"Expected {input.Members.Count} members but got {members.Count}");

        // Assert — JoinedAt is in ascending order for every consecutive pair
        for (var i = 0; i < members.Count - 1; i++)
        {
            Assert.That(members[i].JoinedAt, Is.LessThanOrEqualTo(members[i + 1].JoinedAt),
                $"Members[{i}].JoinedAt ({members[i].JoinedAt}) should be <= Members[{i + 1}].JoinedAt ({members[i + 1].JoinedAt})");
        }
    }
}

/// <summary>
/// Input model for Member List Ordering property test.
/// </summary>
public record MemberListOrderingInput(
    Guid CampaignId,
    Guid RequestingUserId,
    IReadOnlyList<CampaignMember> Members);

/// <summary>
/// Custom FsCheck arbitraries for Member List Ordering test.
/// Generates a campaign with N members (1–10) with random JoinedAt dates,
/// ensuring the requesting user is one of the members.
/// </summary>
public class MemberListOrderingArbitraries
{
    public static Arbitrary<MemberListOrderingInput> MemberListOrderingInputs()
    {
        var roleGen = Gen.Elements(CampaignRole.GM, CampaignRole.Player, CampaignRole.Observer);

        var inputGen =
            from campaignId in ArbMap.Default.GeneratorFor<Guid>()
            from memberCount in Gen.Choose(1, 10)
            from userIds in ArbMap.Default.GeneratorFor<Guid>().ArrayOf(memberCount)
            from roles in roleGen.ArrayOf(memberCount)
            from ticks in Gen.Choose(0, 100_000).ArrayOf(memberCount)
            let baseDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
            let members = userIds.Select((userId, i) => new CampaignMember
            {
                Id = Guid.NewGuid(),
                CampaignId = campaignId,
                UserId = userId,
                Role = roles[i],
                JoinedAt = baseDate.AddMinutes(ticks[i])
            }).ToList()
            // Ensure requesting user is one of the members
            let requestingUserId = members[0].UserId
            select new MemberListOrderingInput(campaignId, requestingUserId, members);

        return inputGen.ToArbitrary();
    }
}
