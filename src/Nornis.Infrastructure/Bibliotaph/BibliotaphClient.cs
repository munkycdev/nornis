using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Bibliotaph;
using Nornis.Application.Configuration;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Bibliotaph;

/// <summary>
/// Bibliotaph's /api/nornis/v1, over a typed <see cref="HttpClient"/> whose base address is
/// <c>Bibliotaph:BaseUrl</c>. This is where a Bibliotaph refusal becomes Nornis state: a refused
/// grant marks the connection Needs reconnect, and every answer becomes a
/// <see cref="BibliotaphFailure"/> or a value. No call retries.
/// </summary>
public sealed class BibliotaphClient : IBibliotaphClient
{
    private const string Prefix = "api/nornis/v1/";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IBibliotaphConnectionRepository _connections;
    private readonly IBibliotaphTokenSource _tokens;
    private readonly BibliotaphOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<BibliotaphClient> _logger;

    public BibliotaphClient(
        HttpClient http,
        IBibliotaphConnectionRepository connections,
        IBibliotaphTokenSource tokens,
        IOptions<BibliotaphOptions> options,
        TimeProvider time,
        ILogger<BibliotaphClient> logger)
    {
        _http = http;
        _connections = connections;
        _tokens = tokens;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public Task<BibliotaphResult<BibliotaphAccepted>> SendWorldEventsAsync(
        Guid connectionUserId, IReadOnlyList<BibliotaphWorldEventMessage> events, CancellationToken ct)
    {
        if (events.Count is 0 or > BibliotaphOptions.MaxEventsPerCall)
        {
            throw new ArgumentOutOfRangeException(nameof(events), events.Count,
                $"A webhook batch holds 1 to {BibliotaphOptions.MaxEventsPerCall} events.");
        }

        return SendAsync(connectionUserId, "world-events", _options.WebhookTimeoutSeconds,
            () => Post("worlds/events", events), ReadNothing, ct);
    }

    public Task<BibliotaphResult<BibliotaphBookList>> ListBooksAsync(Guid connectionUserId, Guid worldId, CancellationToken ct) =>
        SendAsync(connectionUserId, "books", _options.BooksTimeoutSeconds,
            () => new HttpRequestMessage(HttpMethod.Get, Prefix + $"worlds/{worldId}/books"),
            ReadJson<BibliotaphBookList>, ct);

    public Task<BibliotaphResult<BibliotaphCover>> GetCoverAsync(Guid connectionUserId, string coverName, CancellationToken ct) =>
        SendAsync(connectionUserId, "cover", _options.CoverTimeoutSeconds,
            () => new HttpRequestMessage(HttpMethod.Get, Prefix + "covers/" + Uri.EscapeDataString(coverName)),
            ReadCoverAsync, ct);

    public Task<BibliotaphResult<BibliotaphPassages>> FindPassagesAsync(
        Guid connectionUserId, Guid worldId, BibliotaphPassagesRequest request, CancellationToken ct) =>
        SendAsync(connectionUserId, "passages", _options.PassagesTimeoutSeconds,
            () => Post($"worlds/{worldId}/passages", request),
            ReadJson<BibliotaphPassages>, ct);

    public Task<BibliotaphResult<BibliotaphPageTexts>> GetPagesAsync(
        Guid connectionUserId, Guid worldId, string documentId, int fromIndex, int toIndex, CancellationToken ct) =>
        SendAsync(connectionUserId, "pages", _options.PagesTimeoutSeconds,
            () => new HttpRequestMessage(HttpMethod.Get,
                Prefix + $"worlds/{worldId}/documents/{Uri.EscapeDataString(documentId)}/pages?from={fromIndex}&to={toIndex}"),
            ReadJson<BibliotaphPageTexts>, ct);

    public Task<BibliotaphResult<BibliotaphTicket>> MintReadingTicketAsync(
        Guid connectionUserId, Guid worldId, BibliotaphTicketRequest request, CancellationToken ct) =>
        SendAsync(connectionUserId, "reading-ticket", _options.TicketTimeoutSeconds,
            () => Post($"worlds/{worldId}/reading-tickets", request),
            ReadJson<BibliotaphTicket>, ct);

    public Task<BibliotaphResult<BibliotaphAccepted>> WarmAsync(Guid connectionUserId, CancellationToken ct) =>
        SendAsync(connectionUserId, "warm", _options.WarmUpTimeoutSeconds,
            () => Post<object?>("warm", null), ReadNothing, ct);

    private async Task<BibliotaphResult<T>> SendAsync<T>(
        Guid connectionUserId,
        string operation,
        int timeoutSeconds,
        Func<HttpRequestMessage> build,
        Func<HttpResponseMessage, CancellationToken, Task<T>> read,
        CancellationToken ct)
    {
        var connection = await _connections.GetByUserIdAsync(connectionUserId, ct);
        if (connection is null)
        {
            return BibliotaphResult<T>.Fail(BibliotaphFailure.NoConnection);
        }

        if (connection.Status == BibliotaphConnectionStatus.NeedsReconnect)
        {
            return BibliotaphResult<T>.Fail(BibliotaphFailure.NotConnected);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            var accessToken = await _tokens.GetAccessTokenAsync(connection, timeout.Token);
            if (accessToken is null)
            {
                await RefuseAsync(connection, operation, "token_refused", ct);
                return BibliotaphResult<T>.Fail(BibliotaphFailure.NotConnected);
            }

            using var request = build();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            if (request.Method != HttpMethod.Get)
            {
                // Bibliotaph's header rule for writes, kept uniform across its callers.
                request.Headers.Add("X-Bibliotaph", "nornis");
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            if (response.IsSuccessStatusCode)
            {
                var value = await read(response, timeout.Token);
                await _connections.TouchLastUsedAsync(connection.Id, _time.GetUtcNow(),
                    _time.GetUtcNow().AddMinutes(-_options.LastUsedWriteMinutes), ct);
                return BibliotaphResult<T>.Success(value);
            }

            var code = await ReadProblemCodeAsync(response, timeout.Token);
            var failure = Classify(response.StatusCode, code);
            if (failure == BibliotaphFailure.NotConnected)
            {
                _tokens.Forget(connection.Id);
                await RefuseAsync(connection, operation, code ?? ((int)response.StatusCode).ToString(), ct);
            }
            else if (failure is BibliotaphFailure.Unreachable or BibliotaphFailure.Rejected)
            {
                _logger.LogWarning(
                    "Bibliotaph call failed. Operation={Operation}, Status={Status}, Code={Code}, ConnectionId={ConnectionId}",
                    operation, (int)response.StatusCode, code, connection.Id);
            }

            return BibliotaphResult<T>.Fail(failure);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Bibliotaph call timed out. Operation={Operation}, TimeoutSeconds={Timeout}, ConnectionId={ConnectionId}",
                operation, timeoutSeconds, connection.Id);
            return BibliotaphResult<T>.Fail(BibliotaphFailure.Unreachable);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "Bibliotaph call failed. Operation={Operation}, ConnectionId={ConnectionId}",
                operation, connection.Id);
            return BibliotaphResult<T>.Fail(BibliotaphFailure.Unreachable);
        }
    }

    /// <summary>
    /// The contract's refusals, by status and stable code. A 401, or a 403 that isn't a kept-from-AI
    /// book (a missing scope), means the grant no longer does what Nornis needs, and only the GM
    /// reconnecting fixes that.
    /// </summary>
    internal static BibliotaphFailure Classify(HttpStatusCode status, string? code) => status switch
    {
        HttpStatusCode.Unauthorized => BibliotaphFailure.NotConnected,
        HttpStatusCode.Forbidden when code == "kept_from_ai" => BibliotaphFailure.KeptFromAi,
        HttpStatusCode.Forbidden => BibliotaphFailure.NotConnected,
        HttpStatusCode.NotFound => BibliotaphFailure.NotFound,
        HttpStatusCode.ServiceUnavailable when code == "opening" => BibliotaphFailure.Opening,
        >= HttpStatusCode.InternalServerError => BibliotaphFailure.Unreachable,
        HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout => BibliotaphFailure.Unreachable,
        _ => BibliotaphFailure.Rejected,
    };

    private async Task RefuseAsync(BibliotaphConnection connection, string operation, string reason, CancellationToken ct)
    {
        _logger.LogWarning(
            "Bibliotaph refused the grant; connection needs reconnecting. Operation={Operation}, Reason={Reason}, ConnectionId={ConnectionId}, UserId={UserId}",
            operation, reason, connection.Id, connection.UserId);
        await _connections.MarkNeedsReconnectAsync(connection.Id, _time.GetUtcNow(), ct);
    }

    private static HttpRequestMessage Post<TBody>(string path, TBody body) =>
        new(HttpMethod.Post, Prefix + path) { Content = JsonContent.Create(body, options: Json) };

    private static Task<BibliotaphAccepted> ReadNothing(HttpResponseMessage response, CancellationToken ct) =>
        Task.FromResult(default(BibliotaphAccepted));

    private static async Task<T> ReadJson<T>(HttpResponseMessage response, CancellationToken ct) =>
        await response.Content.ReadFromJsonAsync<T>(Json, ct)
        ?? throw new JsonException($"Bibliotaph answered with an empty {typeof(T).Name}.");

    private async Task<BibliotaphCover> ReadCoverAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var max = _options.MaxCoverBytes;
        if (response.Content.Headers.ContentLength > max)
        {
            throw new HttpRequestException($"Cover larger than {max} bytes.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > max)
            {
                throw new HttpRequestException($"Cover larger than {max} bytes.");
            }

            buffer.Write(chunk, 0, read);
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        return new BibliotaphCover(buffer.ToArray(), contentType);
    }

    /// <summary>The stable <c>code</c> of a problem response, or null when there is none to read.</summary>
    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body) || body.Length > 16_384)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("code", out var code)
                   && code.ValueKind == JsonValueKind.String
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
