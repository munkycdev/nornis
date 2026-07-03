using System.Net.Http.Headers;

namespace Nornis.Api.Tests.Infrastructure;

/// <summary>
/// Extension methods for configuring HttpClient with test authentication tokens.
/// </summary>
public static class HttpClientExtensions
{
    /// <summary>
    /// Sets the Authorization header with the specified bearer token.
    /// </summary>
    public static HttpClient WithAuthToken(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Removes the Authorization header, simulating an anonymous request.
    /// </summary>
    public static HttpClient WithoutAuth(this HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }
}
