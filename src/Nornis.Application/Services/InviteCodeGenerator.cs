using System.Security.Cryptography;

namespace Nornis.Application.Services;

/// <summary>
/// Generates invite codes from 16 cryptographically-random bytes rendered as Base64Url
/// (~22 chars, URL- and route-safe: only <c>A-Z a-z 0-9 - _</c>, no padding).
/// </summary>
public class InviteCodeGenerator : IInviteCodeGenerator
{
    public string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}
