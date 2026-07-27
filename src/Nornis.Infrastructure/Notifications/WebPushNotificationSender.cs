using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Notifications;
using Nornis.Domain.Repositories;
using WebPush;

namespace Nornis.Infrastructure.Notifications;

/// <summary>
/// Sends notifications over the Web Push protocol to every browser a person has subscribed.
///
/// Two rules run through all of it. First, a notification never breaks the thing that prompted
/// it: every failure is logged and swallowed, because an extraction that succeeded must not be
/// reported as failed just because a phone was unreachable. Second, the push services are the
/// authority on which subscriptions still exist — when one answers 404 or 410 the browser is
/// gone for good and the row is deleted, which is the only thing that stops the table filling
/// with endpoints that can never be delivered to again.
/// </summary>
public class WebPushNotificationSender : INotificationSender
{
    private readonly IPushSubscriptionRepository _subscriptions;
    private readonly WebPushOptions _options;
    private readonly ILogger<WebPushNotificationSender> _logger;

    public WebPushNotificationSender(
        IPushSubscriptionRepository subscriptions,
        IOptions<WebPushOptions> options,
        ILogger<WebPushNotificationSender> logger)
    {
        _subscriptions = subscriptions;
        _options = options.Value;
        _logger = logger;
    }

    public Task NotifyUserAsync(Guid userId, NotificationMessage message, CancellationToken ct = default) =>
        NotifyUsersAsync([userId], message, ct);

    public async Task NotifyUsersAsync(
        IReadOnlyList<Guid> userIds, NotificationMessage message, CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
        {
            // Expected on a developer machine with no keys. Debug, not warning: this would
            // otherwise fire on every extraction and train people to ignore the log.
            _logger.LogDebug("Web push is not configured; skipping notification '{Title}'.", message.Title);
            return;
        }

        var distinctUserIds = userIds.Distinct().ToList();
        if (distinctUserIds.Count == 0)
        {
            return;
        }

        IReadOnlyList<Domain.Entities.PushSubscription> targets;
        try
        {
            targets = await _subscriptions.ListByUsersAsync(distinctUserIds, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read push subscriptions; notification not sent.");
            return;
        }

        if (targets.Count == 0)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            title = message.Title,
            body = message.Body,
            url = message.Url,
            tag = message.Tag,
        });

        var vapid = new VapidDetails(_options.Subject, _options.PublicKey, _options.PrivateKey);
        var client = new WebPushClient();

        foreach (var target in targets)
        {
            await SendOneAsync(client, vapid, target, payload, message, ct);
        }
    }

    private async Task SendOneAsync(
        WebPushClient client,
        VapidDetails vapid,
        Domain.Entities.PushSubscription target,
        string payload,
        NotificationMessage message,
        CancellationToken ct)
    {
        try
        {
            var subscription = new WebPush.PushSubscription(target.Endpoint, target.P256dh, target.Auth);
            await client.SendNotificationAsync(subscription, payload, vapid, ct);
            await _subscriptions.TouchAsync(target.Id, DateTimeOffset.UtcNow, ct);
        }
        catch (WebPushException ex) when (
            ex.StatusCode == HttpStatusCode.NotFound || ex.StatusCode == HttpStatusCode.Gone)
        {
            // The push service is telling us this browser is never coming back — permission
            // revoked, site data cleared, browser uninstalled. Keeping the row would mean
            // failing this same send forever.
            _logger.LogInformation(
                "Push endpoint is gone; forgetting subscription. SubscriptionId={SubscriptionId}, UserId={UserId}",
                target.Id, target.UserId);

            try
            {
                await _subscriptions.DeleteByEndpointAsync(target.Endpoint, ct);
            }
            catch (Exception deleteEx)
            {
                _logger.LogError(deleteEx,
                    "Could not delete a dead push subscription. SubscriptionId={SubscriptionId}", target.Id);
            }
        }
        catch (Exception ex)
        {
            // Anything else — a rate limit, a payload too large, the service being down — is
            // this one delivery's problem and nobody else's.
            _logger.LogWarning(ex,
                "Push notification failed. SubscriptionId={SubscriptionId}, UserId={UserId}, Title={Title}",
                target.Id, target.UserId, message.Title);
        }
    }
}
