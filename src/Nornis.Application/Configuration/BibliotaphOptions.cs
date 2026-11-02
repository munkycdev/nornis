namespace Nornis.Application.Configuration;

/// <summary>
/// The Bibliotaph integration (feature 31): where Bibliotaph is, the Auth0 application Nornis
/// calls it as, and every bound on talking to it. With any of the four identity settings unset
/// the integration is off: no Profile section, no events, no calls.
/// <c>BibliotaphOptionsTests</c> pins the defaults.
/// </summary>
public class BibliotaphOptions
{
    public const string SectionName = "Bibliotaph";

    /// <summary>Bibliotaph's origin, e.g. <c>https://bibliotaph.online</c>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The Bibliotaph API's Auth0 identifier, e.g. <c>https://bibliotaph.online/api</c>.</summary>
    public string Audience { get; set; } = "https://bibliotaph.online/api";

    /// <summary>The MunkyC tenant's domain, e.g. <c>auth.munkyc.dev</c>. The token endpoint lives here.</summary>
    public string? Auth0Domain { get; set; }

    /// <summary>The "Nornis → Bibliotaph" Auth0 application. Confidential; refresh-token rotation off.</summary>
    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    /// <summary>
    /// Bibliotaph's own Auth0 application, whose tokens for the Nornis API may reach only
    /// <c>api/bibliotaph/v1</c>. API only.
    /// </summary>
    public string? ProviderClientId { get; set; }

    /// <summary>Nornis's web origin, for the addresses Bibliotaph sends the browser back to. API only.</summary>
    public string? WebOrigin { get; set; }

    /// <summary>
    /// Where Auth0 sends the browser back after the GM consents: the API's callback, which is the
    /// one registered on the "Nornis → Bibliotaph" application. API only.
    /// </summary>
    public string CallbackUrl { get; set; } = "https://api.nornis.app/bibliotaph/callback";

    /// <summary>The scopes Nornis asks Bibliotaph for, as the contract names them.</summary>
    public const string Scopes = "openid books:read tickets:write worlds:write offline_access";

    /// <summary>How long a GM has to finish connecting once they press Connect.</summary>
    public int ConnectStateMinutes { get; set; } = 10;

    /// <summary>
    /// Where the Data Protection ring that protects the stored refresh tokens lives: a blob in a
    /// container only the API and Worker identities are granted on. Required when configured.
    /// </summary>
    public string? KeyBlobUri { get; set; }

    public int PassagesTimeoutSeconds { get; set; } = 4;

    public int BooksTimeoutSeconds { get; set; } = 5;

    public int PagesTimeoutSeconds { get; set; } = 8;

    public int TicketTimeoutSeconds { get; set; } = 5;

    public int CoverTimeoutSeconds { get; set; } = 5;

    public int MaxCoverBytes { get; set; } = 2 * 1024 * 1024;

    public int WarmUpTimeoutSeconds { get; set; } = 2;

    public int WebhookTimeoutSeconds { get; set; } = 15;

    public int TokenTimeoutSeconds { get; set; } = 5;

    /// <summary>A world's Library is read from Bibliotaph at most this often.</summary>
    public int ReconcileThrottleSeconds { get; set; } = 60;

    /// <summary>A world's library is warmed at most this often.</summary>
    public int WarmUpThrottleMinutes { get; set; } = 10;

    public int OutboxPollSeconds { get; set; } = 15;

    /// <summary>Events per webhook call; the contract allows at most 100.</summary>
    public int OutboxBatchSize { get; set; } = 100;

    /// <summary>An event not delivered within this many days is given up.</summary>
    public int OutboxMaxAgeDays { get; set; } = 7;

    /// <summary>Delivered and failed events are deleted after this many days.</summary>
    public int OutboxRetentionDays { get; set; } = 7;

    public int ReadingRecordRetentionDays { get; set; } = 90;

    public int MaxExcerptPages { get; set; } = 12;

    /// <summary>A connection's last-used time is written at most this often.</summary>
    public int LastUsedWriteMinutes { get; set; } = 5;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(Auth0Domain)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret);

    /// <summary>
    /// The first bound that is zero or less, by name, or null when every bound is positive. No
    /// bound has an "unbounded" value, so the hosts refuse to start on one.
    /// </summary>
    public string? FirstNonPositiveBound() =>
        new (string Name, int Value)[]
        {
            (nameof(PassagesTimeoutSeconds), PassagesTimeoutSeconds),
            (nameof(BooksTimeoutSeconds), BooksTimeoutSeconds),
            (nameof(PagesTimeoutSeconds), PagesTimeoutSeconds),
            (nameof(TicketTimeoutSeconds), TicketTimeoutSeconds),
            (nameof(CoverTimeoutSeconds), CoverTimeoutSeconds),
            (nameof(MaxCoverBytes), MaxCoverBytes),
            (nameof(WarmUpTimeoutSeconds), WarmUpTimeoutSeconds),
            (nameof(WebhookTimeoutSeconds), WebhookTimeoutSeconds),
            (nameof(TokenTimeoutSeconds), TokenTimeoutSeconds),
            (nameof(ReconcileThrottleSeconds), ReconcileThrottleSeconds),
            (nameof(WarmUpThrottleMinutes), WarmUpThrottleMinutes),
            (nameof(OutboxPollSeconds), OutboxPollSeconds),
            (nameof(OutboxBatchSize), OutboxBatchSize),
            (nameof(OutboxMaxAgeDays), OutboxMaxAgeDays),
            (nameof(OutboxRetentionDays), OutboxRetentionDays),
            (nameof(ReadingRecordRetentionDays), ReadingRecordRetentionDays),
            (nameof(MaxExcerptPages), MaxExcerptPages),
            (nameof(LastUsedWriteMinutes), LastUsedWriteMinutes),
            (nameof(ConnectStateMinutes), ConnectStateMinutes),
        }.FirstOrDefault(b => b.Value <= 0).Name;

    /// <summary>
    /// Why these options can't start a host, or null when they can: a non-positive bound, a batch
    /// over the contract's 100, or a configured integration with nowhere to keep its keys.
    /// </summary>
    public string? StartupProblem()
    {
        if (FirstNonPositiveBound() is { } bound)
        {
            return $"Bibliotaph:{bound} must be greater than zero; no Bibliotaph bound means unbounded.";
        }

        if (OutboxBatchSize > MaxEventsPerCall)
        {
            return $"Bibliotaph:OutboxBatchSize can't exceed {MaxEventsPerCall}, the webhook's limit.";
        }

        if (IsConfigured && string.IsNullOrWhiteSpace(KeyBlobUri))
        {
            return "Bibliotaph is configured but Bibliotaph:KeyBlobUri is not. Without it the stored " +
                   "grants can't be read after a deploy, and every GM would have to reconnect.";
        }

        return null;
    }

    /// <summary>The webhook's batch limit, from the contract.</summary>
    public const int MaxEventsPerCall = 100;
}
