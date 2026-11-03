using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using Nornis.Domain.Enums;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Checks a key by listing models with it (feature 39): free, and every OpenAI-protocol endpoint
/// has it. The HTTP client is the factory's, on <see cref="PublicEndpointGuard"/>'s handler, so a
/// compatible endpoint's base URL can't point Nornis at a private address. The key is sent as a
/// header and never logged: the factory's logging redacts header values, which a test holds.
/// </summary>
public sealed class AiKeyChecker : IAiKeyChecker
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static readonly Uri OpenAiEndpoint = new("https://api.openai.com/v1/");

    private readonly HttpClient _http;
    private readonly ILogger<AiKeyChecker> _logger;

    public AiKeyChecker(HttpClient http, ILogger<AiKeyChecker> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<AiKeyCheckOutcome> CheckAsync(AiProvider provider, string key, Uri? baseUrl, CancellationToken ct)
    {
        var endpoint = provider switch
        {
            AiProvider.OpenAi => OpenAiEndpoint,
            AiProvider.OpenAiCompatible => baseUrl ?? throw new ArgumentNullException(nameof(baseUrl)),
            _ => throw new NotSupportedException($"No key check for {provider} yet (feature 39 Phase C)."),
        };

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "models"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AiKeyCheckOutcome.Rejected,
                HttpStatusCode.NotFound => AiKeyCheckOutcome.NotFound,
                _ when response.IsSuccessStatusCode => AiKeyCheckOutcome.Accepted,
                _ => Unreachable(provider, $"HTTP {(int)response.StatusCode}"),
            };
        }
        catch (HttpRequestException ex)
        {
            return Unreachable(provider, ex.Message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Unreachable(provider, $"no answer within {Timeout.TotalSeconds:0}s");
        }
    }

    private AiKeyCheckOutcome Unreachable(AiProvider provider, string why)
    {
        _logger.LogInformation("AI key check couldn't reach {Provider}: {Why}", provider, why);
        return AiKeyCheckOutcome.Unreachable;
    }
}
