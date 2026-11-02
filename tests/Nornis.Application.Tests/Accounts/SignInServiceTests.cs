using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Accounts;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;
using NUnit.Framework;

namespace Nornis.Application.Tests.Accounts;

/// <summary>Several sign-ins per account (feature 33): linking, claiming and removing.</summary>
[TestFixture]
public class SignInServiceTests
{
    private const string VossDiscord = "oauth2|discord|voss";
    private const string VossGoogle = "google-oauth2|voss";
    private const string TavrinGoogle = "google-oauth2|tavrin";

    private readonly Guid _voss = Guid.NewGuid();
    private readonly Guid _tavrin = Guid.NewGuid();

    private FakeSignIns _signIns = null!;
    private Clock _time = null!;

    [SetUp]
    public void SetUp()
    {
        _signIns = new FakeSignIns();
        _signIns.Rows.Add(new UserSignIn { Id = Guid.NewGuid(), UserId = _voss, Subject = VossDiscord, AddedAt = new DateTimeOffset(2026, 7, 3, 0, 0, 0, TimeSpan.Zero) });
        _signIns.Rows.Add(new UserSignIn { Id = Guid.NewGuid(), UserId = _tavrin, Subject = TavrinGoogle, AddedAt = new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero) });
        _time = new Clock();
    }

    private SignInService Sut() => new(_signIns, _time, NullLogger<SignInService>.Instance);

    [Test]
    public async Task A_claimed_code_adds_the_new_sign_in_to_the_user_who_began()
    {
        var code = await Sut().BeginLinkAsync(_voss, CancellationToken.None);

        var result = await Sut().ClaimAsync(code, VossGoogle, CancellationToken.None);

        Assert.That(result.Value, Is.EqualTo(SignInLinkOutcome.Linked));
        Assert.That(_signIns.Rows.Where(s => s.UserId == _voss).Select(s => s.Subject), Is.EquivalentTo([VossDiscord, VossGoogle]));
        Assert.That(_signIns.Rows.Single(s => s.Subject == VossGoogle).AddedAt, Is.EqualTo(_time.Now));
    }

    [Test]
    public async Task Only_the_codes_hash_is_kept()
    {
        var code = await Sut().BeginLinkAsync(_voss, CancellationToken.None);

        var stored = _signIns.Codes.Single();
        Assert.That(stored.CodeHash, Is.Not.EqualTo(code));
        Assert.That(stored.CodeHash, Has.Length.EqualTo(64));
        Assert.That(stored.ExpiresAt, Is.EqualTo(_time.Now + SignInService.LinkCodeLifetime));
    }

    [Test]
    public async Task A_new_code_replaces_the_one_before()
    {
        var first = await Sut().BeginLinkAsync(_voss, CancellationToken.None);
        var second = await Sut().BeginLinkAsync(_voss, CancellationToken.None);

        Assert.That(_signIns.Codes, Has.Count.EqualTo(1));
        Assert.That((await Sut().ClaimAsync(first, VossGoogle, CancellationToken.None)).Error!.Code, Is.EqualTo("link_expired"));
        Assert.That((await Sut().ClaimAsync(second, VossGoogle, CancellationToken.None)).Value, Is.EqualTo(SignInLinkOutcome.Linked));
    }

    [Test]
    public async Task A_code_is_good_once()
    {
        var code = await Sut().BeginLinkAsync(_voss, CancellationToken.None);
        await Sut().ClaimAsync(code, VossGoogle, CancellationToken.None);

        var again = await Sut().ClaimAsync(code, "windowslive|voss", CancellationToken.None);

        Assert.That(again.Error!.Code, Is.EqualTo("link_expired"));
        Assert.That(_signIns.Rows.Any(s => s.Subject == "windowslive|voss"), Is.False);
    }

    [Test]
    public async Task An_expired_code_adds_nothing_and_is_spent()
    {
        var code = await Sut().BeginLinkAsync(_voss, CancellationToken.None);
        _time.Now += SignInService.LinkCodeLifetime;

        var result = await Sut().ClaimAsync(code, VossGoogle, CancellationToken.None);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("link_expired"));
        Assert.That(_signIns.Rows.Any(s => s.Subject == VossGoogle), Is.False);
        Assert.That(_signIns.Codes, Is.Empty);
    }

    [TestCase("")]
    [TestCase("not-a-code-anyone-was-given")]
    public async Task An_unknown_code_adds_nothing(string code)
    {
        var result = await Sut().ClaimAsync(code, VossGoogle, CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("link_expired"));
        Assert.That(_signIns.Rows, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Another_users_sign_in_is_refused_and_nothing_moves()
    {
        var code = await Sut().BeginLinkAsync(_voss, CancellationToken.None);

        var result = await Sut().ClaimAsync(code, TavrinGoogle, CancellationToken.None);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
        Assert.That(result.Error.Code, Is.EqualTo("sign_in_taken"));
        Assert.That(result.Error.Message, Does.Contain("Google"));
        Assert.That(_signIns.Rows.Single(s => s.Subject == TavrinGoogle).UserId, Is.EqualTo(_tavrin));
        Assert.That(_signIns.Codes, Is.Empty, "a refused claim still spends the code");
    }

    [Test]
    public async Task The_users_own_sign_in_is_already_linked()
    {
        var code = await Sut().BeginLinkAsync(_voss, CancellationToken.None);

        var result = await Sut().ClaimAsync(code, VossDiscord, CancellationToken.None);

        Assert.That(result.Value, Is.EqualTo(SignInLinkOutcome.AlreadyLinked));
        Assert.That(_signIns.Rows, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task The_list_names_providers_oldest_first_and_marks_the_session()
    {
        _signIns.Rows.Add(new UserSignIn { Id = Guid.NewGuid(), UserId = _voss, Subject = VossGoogle, AddedAt = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero) });

        var list = await Sut().ListAsync(_voss, VossGoogle, CancellationToken.None);

        Assert.That(list.Select(v => (v.Provider, v.Current)), Is.EqualTo([("Discord", false), ("Google", true)]));
    }

    [Test]
    public async Task Removing_a_sign_in_other_than_the_sessions_deletes_it_and_names_its_subject()
    {
        var google = new UserSignIn { Id = Guid.NewGuid(), UserId = _voss, Subject = VossGoogle };
        _signIns.Rows.Add(google);

        var result = await Sut().RemoveAsync(_voss, google.Id, VossDiscord, CancellationToken.None);

        Assert.That(result.Value, Is.EqualTo(VossGoogle), "the controller evicts this subject from the cache");
        Assert.That(_signIns.Rows.Any(s => s.Id == google.Id), Is.False);
    }

    [Test]
    public async Task The_sessions_own_sign_in_is_never_removed()
    {
        var google = new UserSignIn { Id = Guid.NewGuid(), UserId = _voss, Subject = VossGoogle };
        _signIns.Rows.Add(google);

        var result = await Sut().RemoveAsync(_voss, google.Id, VossGoogle, CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("sign_in_in_use"));
        Assert.That(_signIns.Rows.Any(s => s.Id == google.Id), Is.True);
    }

    [Test]
    public async Task The_last_sign_in_is_never_removed()
    {
        var discord = _signIns.Rows.Single(s => s.Subject == VossDiscord);

        // Signed in some other way than the one sign-in left, which can't happen in practice
        // but must still not leave an account no one can reach.
        var result = await Sut().RemoveAsync(_voss, discord.Id, "windowslive|elsewhere", CancellationToken.None);

        Assert.That(result.Error!.Code, Is.EqualTo("last_sign_in"));
        Assert.That(_signIns.Rows.Any(s => s.Id == discord.Id), Is.True);
    }

    [Test]
    public async Task Another_users_sign_in_is_not_found()
    {
        var tavrins = _signIns.Rows.Single(s => s.Subject == TavrinGoogle);
        _signIns.Rows.Add(new UserSignIn { Id = Guid.NewGuid(), UserId = _voss, Subject = VossGoogle });

        var result = await Sut().RemoveAsync(_voss, tavrins.Id, VossDiscord, CancellationToken.None);

        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(_signIns.Rows.Any(s => s.Id == tavrins.Id), Is.True);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 2, 20, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>The repository's contract over lists, so the rules are tested rather than the mocks.</summary>
    private sealed class FakeSignIns : IUserSignInRepository
    {
        public List<UserSignIn> Rows { get; } = [];

        public List<SignInLinkCode> Codes { get; } = [];

        public Task<IReadOnlyList<UserSignIn>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserSignIn>>(Rows.Where(s => s.UserId == userId).OrderBy(s => s.AddedAt).ToList());

        public Task<UserSignIn?> GetBySubjectAsync(string subject, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.FirstOrDefault(s => s.Subject == subject));

        public Task AddAsync(UserSignIn signIn, CancellationToken cancellationToken = default)
        {
            if (Rows.Any(s => s.Subject == signIn.Subject))
            {
                throw new InvalidOperationException("unique index on Subject");
            }

            Rows.Add(signIn);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid signInId, CancellationToken cancellationToken = default)
        {
            Rows.RemoveAll(s => s.Id == signInId);
            return Task.CompletedTask;
        }

        public Task SetLinkCodeAsync(Guid userId, string codeHash, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
        {
            Codes.RemoveAll(c => c.UserId == userId);
            Codes.Add(new SignInLinkCode { UserId = userId, CodeHash = codeHash, ExpiresAt = expiresAt });
            return Task.CompletedTask;
        }

        public Task<SignInLinkCode?> TakeLinkCodeAsync(string codeHash, CancellationToken cancellationToken = default)
        {
            var code = Codes.FirstOrDefault(c => c.CodeHash == codeHash);
            if (code is not null)
            {
                Codes.Remove(code);
            }

            return Task.FromResult(code);
        }
    }
}
