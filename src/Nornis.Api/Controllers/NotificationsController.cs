using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Domain.Repositories;
using Nornis.Infrastructure.Notifications;

namespace Nornis.Api.Controllers;

/// <summary>
/// Push notification subscriptions. Not world-scoped: permission is something a person grants
/// a browser once, and the notification itself says which world it came from.
/// </summary>
[ApiController]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly IPushSubscriptionRepository _subscriptions;
    private readonly WebPushOptions _options;

    public NotificationsController(
        IPushSubscriptionRepository subscriptions, IOptions<WebPushOptions> options)
    {
        _subscriptions = subscriptions;
        _options = options.Value;
    }

    /// <summary>
    /// What the browser needs before it can subscribe. The public key is not a secret — it is
    /// handed to every subscriber by design. Reports whether the server can send at all, so the
    /// UI can say "not configured" instead of offering a button that silently does nothing.
    /// </summary>
    [HttpGet("config")]
    public IActionResult GetConfig() =>
        Ok(new PushConfigResponse(_options.IsConfigured, _options.PublicKey));

    [HttpGet("subscriptions")]
    public async Task<IActionResult> ListSubscriptions(CancellationToken ct)
    {
        var user = HttpContext.GetNornisUser();
        var subscriptions = await _subscriptions.ListByUserAsync(user.Id, ct);

        return Ok(subscriptions
            .Select(s => new PushSubscriptionResponse(s.Id, s.Label, s.CreatedAt, s.LastSucceededAt))
            .ToList());
    }

    [HttpPost("subscriptions")]
    public async Task<IActionResult> Subscribe(
        [FromBody] SavePushSubscriptionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint)
            || string.IsNullOrWhiteSpace(request.P256dh)
            || string.IsNullOrWhiteSpace(request.Auth))
        {
            return BadRequest(new ErrorResponse("validation_error",
                "A push subscription needs an endpoint and both keys."));
        }

        var user = HttpContext.GetNornisUser();

        var saved = await _subscriptions.UpsertAsync(new Domain.Entities.PushSubscription
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Endpoint = request.Endpoint,
            P256dh = request.P256dh,
            Auth = request.Auth,
            Label = request.Label,
            CreatedAt = DateTimeOffset.UtcNow
        }, ct);

        return Ok(new PushSubscriptionResponse(saved.Id, saved.Label, saved.CreatedAt, saved.LastSucceededAt));
    }

    /// <summary>
    /// Forgets a browser. Takes the endpoint rather than an id because the browser knows its own
    /// endpoint and not our row id — and because a browser unsubscribing itself is the common case.
    /// </summary>
    [HttpDelete("subscriptions")]
    public async Task<IActionResult> Unsubscribe([FromQuery] string endpoint, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return BadRequest(new ErrorResponse("validation_error", "An endpoint is required."));
        }

        // Deliberately not checking ownership: possessing the endpoint is proof enough, and the
        // worst case is a user turning off notifications for a browser they already control.
        await _subscriptions.DeleteByEndpointAsync(endpoint, ct);
        return NoContent();
    }
}
