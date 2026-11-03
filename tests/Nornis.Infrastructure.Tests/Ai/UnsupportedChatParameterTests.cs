using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// Guards against re-introducing chat parameters the deployed models reject.
///
/// On 2026-07-27 setting <c>ChatCompletionOptions.MaxOutputTokenCount</c> took every AI feature
/// in production down at once. Azure.AI.OpenAI 2.1.0, the SDK in use then, serialised it as
/// <c>max_tokens</c>, and the gpt-5.4 deployments answer:
///
///     HTTP 400 (invalid_request_error: unsupported_parameter) Parameter: max_tokens
///     'max_tokens' is not supported with this model. Use 'max_completion_tokens' instead.
///
/// The same class of rejection applies to <c>Temperature</c> on this model family — which had
/// been silently failing world-name generation for two days before anyone looked, because that
/// call swallows failures and falls back to a static name.
///
/// This scans source rather than reflecting over behaviour because the damage is done at the
/// point someone *writes* the assignment: the failure is a 400 from a live deployment, so no
/// unit test that stops short of a real call would catch it. Since 2026-09 the clients use the
/// plain OpenAI SDK behind Microsoft.Extensions.AI, which may well emit
/// <c>max_completion_tokens</c>; once a real call against the deployment proves it, delete this
/// test along with the restriction.
///
/// 2026-11-02 (feature 29, task A2): that call was made, and the deployment accepts and enforces
/// <c>max_completion_tokens</c>. The restriction is relaxed for one file only, Prep's drafting
/// client, whose ceiling is pinned by <see cref="DraftingOptions_CarryTheCeiling_OnTheSdkOptions"/>.
/// Every other client still sends none, and <c>Temperature</c> stays banned everywhere: nothing
/// has shown this model family accepts it.
///
/// 2026-11-03 (feature 39, Phase A): clients no longer build SDK options. A ceiling is asked for
/// as <c>AiCallOptions.MaxOutputTokens</c> (a named argument, so the scan now catches
/// <c>MaxOutputTokens:</c> too), and only the OpenAI provider traits put it on the wire, as
/// <c>MaxOutputTokenCount</c>. Drafting is still the only client allowed to ask for one.
/// </summary>
[TestFixture]
public class UnsupportedChatParameterTests
{
    private static readonly string[] ForbiddenAssignments =
    [
        "MaxOutputTokenCount",
        // The same parameter under its Microsoft.Extensions.AI name: ChatOptions.MaxOutputTokens is
        // translated into the SDK's MaxOutputTokenCount by the adapter, so it reaches the wire alike.
        "MaxOutputTokens",
        "Temperature",
    ];

    /// <summary>The ceilings a file may set, by file name. Temperature is never on this list.</summary>
    private static readonly Dictionary<string, string[]> AllowedCeilings = new()
    {
        ["AdventureDraftingAiClient.cs"] = ["MaxOutputTokens"],
        // The declaration of the provider-neutral option and its null default; not a value.
        ["AiCallOptions.cs"] = ["MaxOutputTokens"],
        // The one place a ceiling reaches the OpenAI wire, pinned by OutputCeilingWireTests.
        ["OpenAiProviderTraits.cs"] = ["MaxOutputTokenCount"],
    };

    private static DirectoryInfo AiSourceDirectory()
    {
        // Walk up from the test binary to the repo root, then into the AI clients.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Nornis.Infrastructure", "Ai")))
        {
            dir = dir.Parent;
        }

        Assert.That(dir, Is.Not.Null, "could not locate the repository root from the test output directory");
        return new DirectoryInfo(Path.Combine(dir!.FullName, "src", "Nornis.Infrastructure", "Ai"));
    }

    [Test]
    public void NoAiClientSetsAParameterTheDeployedModelsReject()
    {
        var offenders = new List<string>();

        foreach (var file in AiSourceDirectory().GetFiles("*.cs"))
        {
            var lines = File.ReadAllLines(file.FullName);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Comments explaining the restriction are the point of this file, not a breach.
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)
                    || line.TrimStart().StartsWith("///", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var property in ForbiddenAssignments)
                {
                    if (AllowedCeilings.TryGetValue(file.Name, out var allowed) && allowed.Contains(property))
                    {
                        continue;
                    }

                    // An assignment or a named argument, not a mention: `Temperature = 1.2f`,
                    // `MaxOutputTokenCount = X`, or `new AiCallOptions(MaxOutputTokens: X)`.
                    if (Regex.IsMatch(line, $@"\b{property}\s*(=[^=]|:\s)"))
                    {
                        offenders.Add($"{file.Name}:{i + 1}: {line.Trim()}");
                    }
                }
            }
        }

        Assert.That(offenders, Is.Empty,
            "These deployments reject the parameter with HTTP 400 before spending any tokens, so "
            + "the affected feature stops working entirely:\n" + string.Join("\n", offenders));
    }

    [Test]
    public void ExtractionRequestOptions_CarryNoOutputTokenCeiling()
    {
        // The behavioural half: the actual options object the extraction path sends. Extraction
        // is ~88% of AI spend and was the call that surfaced the outage. Both layers are checked:
        // the abstraction's own ceiling, and the SDK options object the adapter starts from,
        // which a ceiling could equally be set on.
        var built = Nornis.Infrastructure.Ai.OpenAiProviderTraits.Instance.ChatOptions(Nornis.Infrastructure.Ai.ExtractionAiClient.CallOptions)!;
        var raw = built.RawRepresentationFactory?.Invoke(new FakeChatClient()) as OpenAI.Chat.ChatCompletionOptions;

        Assert.Multiple(() =>
        {
            Assert.That(built.MaxOutputTokens, Is.Null,
                "the adapter sends this as the output ceiling, which the deployment rejects outright");
            Assert.That(raw, Is.Not.Null, "extraction must still build its SDK options through the factory");
            Assert.That(raw!.MaxOutputTokenCount, Is.Null,
                "setting this serialises as max_tokens, which the deployment rejects outright");
        });
    }

    [Test]
    public void DraftingOptions_CarryTheCeiling_OnTheSdkOptions()
    {
        // The one client allowed a ceiling sets it where OutputCeilingWireTests proved it goes out
        // as max_completion_tokens: on the SDK options, not ChatOptions.MaxOutputTokens.
        var built = Nornis.Infrastructure.Ai.OpenAiProviderTraits.Instance.ChatOptions(Nornis.Infrastructure.Ai.AdventureDraftingAiClient.CallOptions)!;
        var raw = built.RawRepresentationFactory?.Invoke(new FakeChatClient()) as OpenAI.Chat.ChatCompletionOptions;

        Assert.Multiple(() =>
        {
            Assert.That(built.MaxOutputTokens, Is.Null);
            Assert.That(built.Temperature, Is.Null);
            Assert.That(raw, Is.Not.Null);
            Assert.That(raw!.MaxOutputTokenCount, Is.EqualTo(2_500));
            Assert.That(raw.Temperature, Is.Null);
        });
    }

    [Test]
    public void TheAllowance_IsForTheDraftingClientOnly_AndNeverForTemperature()
    {
        // Besides the drafting client, only the option's declaration and the one place that puts
        // it on the OpenAI wire; no other client may ask for a ceiling.
        Assert.Multiple(() =>
        {
            Assert.That(AllowedCeilings.Keys, Is.EquivalentTo(
                ["AdventureDraftingAiClient.cs", "AiCallOptions.cs", "OpenAiProviderTraits.cs"]));
            Assert.That(AllowedCeilings.Values.SelectMany(v => v), Has.None.EqualTo("Temperature"));
        });
    }
}
