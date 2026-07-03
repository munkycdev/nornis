using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Nornis.Api.Tests.Infrastructure;

/// <summary>
/// Generates JWT tokens for integration testing with configurable claims,
/// expiration, and issuer. Uses a static symmetric signing key that the
/// test WebApplicationFactory validates against.
/// </summary>
public static class TestJwtIssuer
{
    public const string Issuer = "https://nornis-test-issuer/";
    public const string Audience = "https://nornis-test-api";
    public const string SigningKey = "nornis-integration-test-signing-key-at-least-32-bytes-long!";

    private static readonly SymmetricSecurityKey SecurityKey =
        new(Encoding.UTF8.GetBytes(SigningKey));

    private static readonly SigningCredentials Credentials =
        new(SecurityKey, SecurityAlgorithms.HmacSha256);

    public static SecurityKey GetSecurityKey() => SecurityKey;

    /// <summary>
    /// Generates a valid JWT token with the specified claims.
    /// </summary>
    public static string GenerateToken(
        string sub,
        string email,
        string? nickname = null,
        TimeSpan? lifetime = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, sub),
            new("sub", sub),
            new(ClaimTypes.Email, email),
            new("email", email)
        };

        if (nickname is not null)
        {
            claims.Add(new Claim("nickname", nickname));
        }

        var expiration = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromHours(1));

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiration,
            Issuer = Issuer,
            Audience = Audience,
            SigningCredentials = Credentials
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(tokenDescriptor);
        return handler.WriteToken(token);
    }

    /// <summary>
    /// Generates an expired JWT token.
    /// </summary>
    public static string GenerateExpiredToken(
        string sub = "auth0|expired-user",
        string email = "expired@example.com")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, sub),
            new("sub", sub),
            new(ClaimTypes.Email, email),
            new("email", email)
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            NotBefore = DateTime.UtcNow.AddHours(-2),
            Expires = DateTime.UtcNow.AddHours(-1),
            Issuer = Issuer,
            Audience = Audience,
            SigningCredentials = Credentials
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(tokenDescriptor);
        return handler.WriteToken(token);
    }

    /// <summary>
    /// Generates a JWT token with an untrusted issuer.
    /// </summary>
    public static string GenerateWrongIssuerToken(
        string sub = "auth0|wrong-issuer-user",
        string email = "wrong-issuer@example.com")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, sub),
            new("sub", sub),
            new(ClaimTypes.Email, email),
            new("email", email)
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = "https://untrusted-issuer.example.com/",
            Audience = Audience,
            SigningCredentials = Credentials
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(tokenDescriptor);
        return handler.WriteToken(token);
    }

    /// <summary>
    /// Generates a JWT token without the email claim.
    /// </summary>
    public static string GenerateTokenWithoutEmail(
        string sub = "auth0|no-email-user",
        string? nickname = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, sub),
            new("sub", sub)
        };

        if (nickname is not null)
        {
            claims.Add(new Claim("nickname", nickname));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = Issuer,
            Audience = Audience,
            SigningCredentials = Credentials
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(tokenDescriptor);
        return handler.WriteToken(token);
    }
}
