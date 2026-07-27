using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// Guards against re-introducing chat parameters the deployed models reject.
///
/// On 2026-07-27 setting <c>ChatCompletionOptions.MaxOutputTokenCount</c> took every AI feature
/// in production down at once. Azure.AI.OpenAI 2.1.0 — still the current release — serialises it
/// as <c>max_tokens</c>, and the gpt-5.4 deployments answer:
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
/// unit test that stops short of a real call would catch it. When the SDK gains
/// <c>max_completion_tokens</c>, delete this test along with the restriction.
/// </summary>
[TestFixture]
public class UnsupportedChatParameterTests
{
    private static readonly string[] ForbiddenAssignments =
    [
        "MaxOutputTokenCount",
        "Temperature",
    ];

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
                    // An assignment, not a mention: `Temperature = 1.2f` or `MaxOutputTokenCount = X`.
                    if (Regex.IsMatch(line, $@"\b{property}\s*=[^=]"))
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
        // is ~88% of AI spend and was the call that surfaced the outage.
        var built = Nornis.Infrastructure.Ai.AzureOpenAiExtractionClient.BuildCompletionOptions();

        Assert.That(built.MaxOutputTokenCount, Is.Null,
            "setting this serialises as max_tokens, which the deployment rejects outright");
    }
}
