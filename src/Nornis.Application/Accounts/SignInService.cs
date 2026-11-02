using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Nornis.Application.Errors;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Accounts;

/// <summary>One of the account's sign-ins as Profile shows it.</summary>
public sealed record SignInView(Guid Id, string Provider, DateTimeOffset AddedAt, bool Current);

/// <summary>How a claimed link ended when it didn't fail.</summary>
public enum SignInLinkOutcome
{
    Linked,
    AlreadyLinked,
}

public interface ISignInService
{
    /// <summary>The user's sign-ins, oldest first, marking <paramref name="currentSubject"/>.</summary>
    Task<IReadOnlyList<SignInView>> ListAsync(Guid userId, string currentSubject, CancellationToken ct);

    /// <summary>A new link code for the user, replacing any they had.</summary>
    Task<string> BeginLinkAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Adds <paramref name="subject"/> to the user the code was issued to. Spends the code
    /// whatever the outcome.
    /// </summary>
    Task<AppResult<SignInLinkOutcome>> ClaimAsync(string code, string subject, CancellationToken ct);

    /// <summary>Removes one of the user's sign-ins, never the one in use and never the last.</summary>
    Task<AppResult<string>> RemoveAsync(Guid userId, Guid signInId, string currentSubject, CancellationToken ct);
}

/// <summary>
/// Several sign-ins per account (feature 33). Every rule about linking and removing lives here:
/// the API controller only passes the caller's user and token subject in.
///
/// A link is two calls. <see cref="BeginLinkAsync"/> is made with the session's token and issues
/// a code bound to that user; the Web carries it through an Auth0 round trip that forces the login
/// screen. <see cref="ClaimAsync"/> is made with the new sign-in's own token and attaches its
/// subject. Nothing is ever matched by email, and a subject another user owns is refused, not
/// merged.
/// </summary>
public sealed class SignInService : ISignInService
{
    /// <summary>How long a link code lasts: long enough to pick a provider and sign in, no longer.</summary>
    public static readonly TimeSpan LinkCodeLifetime = TimeSpan.FromMinutes(10);

    private readonly IUserSignInRepository _signIns;
    private readonly TimeProvider _time;
    private readonly ILogger<SignInService> _logger;

    public SignInService(IUserSignInRepository signIns, TimeProvider time, ILogger<SignInService> logger)
    {
        _signIns = signIns;
        _time = time;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SignInView>> ListAsync(Guid userId, string currentSubject, CancellationToken ct)
    {
        var signIns = await _signIns.ListByUserAsync(userId, ct);
        return signIns
            .Select(s => new SignInView(s.Id, SignInProviders.Name(s.Subject), s.AddedAt,
                string.Equals(s.Subject, currentSubject, StringComparison.Ordinal)))
            .ToList();
    }

    public async Task<string> BeginLinkAsync(Guid userId, CancellationToken ct)
    {
        var code = Base64Url(RandomNumberGenerator.GetBytes(32));
        await _signIns.SetLinkCodeAsync(userId, Hash(code), _time.GetUtcNow() + LinkCodeLifetime, ct);
        return code;
    }

    public async Task<AppResult<SignInLinkOutcome>> ClaimAsync(string code, string subject, CancellationToken ct)
    {
        var issued = string.IsNullOrEmpty(code) ? null : await _signIns.TakeLinkCodeAsync(Hash(code), ct);
        if (issued is null || issued.ExpiresAt <= _time.GetUtcNow())
        {
            return AppResult<SignInLinkOutcome>.Fail(new AppError(400, "link_expired",
                "That link has expired. Start again from your Profile."));
        }

        var owner = await _signIns.GetBySubjectAsync(subject, ct);
        if (owner is not null)
        {
            if (owner.UserId == issued.UserId)
            {
                return AppResult<SignInLinkOutcome>.Success(SignInLinkOutcome.AlreadyLinked);
            }

            // Logged without the subject: which provider is enough to diagnose, and the
            // subject is a person's identifier at another service.
            _logger.LogWarning(
                "Sign-in link refused: the {Provider} sign-in already has its own account. UserId={UserId}",
                SignInProviders.Name(subject), issued.UserId);
            return AppResult<SignInLinkOutcome>.Fail(new AppError(409, "sign_in_taken",
                $"That {SignInProviders.Name(subject)} sign-in already has its own Nornis account."));
        }

        await _signIns.AddAsync(new UserSignIn
        {
            Id = Guid.NewGuid(),
            UserId = issued.UserId,
            Subject = subject,
            AddedAt = _time.GetUtcNow(),
        }, ct);

        _logger.LogInformation("Sign-in linked: {Provider}. UserId={UserId}", SignInProviders.Name(subject), issued.UserId);
        return AppResult<SignInLinkOutcome>.Success(SignInLinkOutcome.Linked);
    }

    public async Task<AppResult<string>> RemoveAsync(Guid userId, Guid signInId, string currentSubject, CancellationToken ct)
    {
        var signIns = await _signIns.ListByUserAsync(userId, ct);
        var target = signIns.FirstOrDefault(s => s.Id == signInId);
        if (target is null)
        {
            return AppResult<string>.Fail(new AppError(404, "not_found", "Sign-in not found."));
        }

        if (string.Equals(target.Subject, currentSubject, StringComparison.Ordinal))
        {
            return AppResult<string>.Fail(new AppError(409, "sign_in_in_use",
                "You're signed in with this one. Sign in another way to remove it."));
        }

        if (signIns.Count == 1)
        {
            return AppResult<string>.Fail(new AppError(409, "last_sign_in",
                "An account needs at least one way to sign in."));
        }

        await _signIns.DeleteAsync(target.Id, ct);
        _logger.LogInformation("Sign-in removed: {Provider}. UserId={UserId}", SignInProviders.Name(target.Subject), userId);
        return AppResult<string>.Success(target.Subject);
    }

    private static string Hash(string code) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
