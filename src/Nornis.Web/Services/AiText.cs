namespace Nornis.Web.Services;

/// <summary>
/// How the Web names AI providers, kinds of AI work and refusals (feature 39). The API sends enum
/// names; this is the one place they become words.
/// </summary>
public static class AiText
{
    /// <summary>The providers a key can be added for today. Anthropic and Gemini join with their
    /// clients (Phase C).</summary>
    public static readonly IReadOnlyList<string> AddableProviders = ["OpenAi", "OpenAiCompatible"];

    /// <summary>The kinds of AI work, in the order Profile shows them.</summary>
    public static readonly IReadOnlyList<string> Tasks = ["Ask", "Extraction", "Prep", "Chores"];

    private static readonly string[] BilledProviders = ["OpenAi", "Anthropic", "Gemini"];

    public const string RequiredCode = "ai_key_required";
    public const string RejectedCode = "ai_key_rejected";
    public const string CreditCode = "ai_credit_exhausted";

    public static string Provider(string provider) => provider switch
    {
        "AzureOpenAi" => "Azure OpenAI",
        "OpenAi" => "OpenAI",
        "Anthropic" => "Anthropic",
        "Gemini" => "Google Gemini",
        "OpenAiCompatible" => "OpenAI-compatible",
        _ => provider,
    };

    /// <summary>Where a provider's keys are made, for the AI keys card.</summary>
    public static string? KeysUrl(string provider) => provider switch
    {
        "OpenAi" => "https://platform.openai.com/api-keys",
        "Anthropic" => "https://console.anthropic.com/settings/keys",
        "Gemini" => "https://aistudio.google.com/apikey",
        _ => null,
    };

    /// <summary>Where a provider account's credit is topped up, for an out-of-credit refusal.</summary>
    public static string? BillingUrl(string? provider) => provider switch
    {
        "OpenAi" => "https://platform.openai.com/settings/organization/billing",
        "Anthropic" => "https://console.anthropic.com/settings/billing",
        "Gemini" => "https://aistudio.google.com/",
        _ => null,
    };

    public static string Task(string task) => task switch
    {
        "Ask" => "Ask and world tools",
        "Extraction" => "Reading sources",
        "Prep" => "Prep",
        "Chores" => "Small jobs",
        _ => task,
    };

    public static string TaskHint(string task) => task switch
    {
        "Ask" => "The Loremaster, continuity checks, digests, recaps and handwriting.",
        "Extraction" => "Turning sources, maps and images into proposals. The most demanding work.",
        "Prep" => "Drafting adventures and talking them through.",
        "Chores" => "Short jobs such as naming and narrating threads.",
        _ => string.Empty,
    };

    /// <summary>
    /// The provider an out-of-credit refusal names, for its billing link. The API's message reads
    /// "The {provider} account behind this world's key…" with the same names as <see cref="Provider"/>;
    /// null when no billable provider is named (an OpenAI-compatible endpoint has no known page).
    /// </summary>
    public static string? CreditProvider(string message) =>
        BilledProviders.FirstOrDefault(p => message.Contains($"The {Provider(p)} account", StringComparison.Ordinal));

    /// <summary>A refusal is about who pays, not a fault: its answer is a key, not "try again".</summary>
    public static bool IsRefusal(string? code) => code is RequiredCode or RejectedCode or CreditCode;

    public static string Money(decimal usd) => usd.ToString("$0.00", System.Globalization.CultureInfo.InvariantCulture);
}
