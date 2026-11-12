using MudBlazor;
using Nornis.Web.ApiClient;

namespace Nornis.Web.Services;

/// <summary>
/// The question asked before a continuity finding is dismissed. A dismissed finding is gone
/// for good and gives its penalty points back, and the control is a small close icon, on World
/// memory and on the entry the finding is about. Both ask through here so the wording, and the
/// fact of asking, cannot differ between them (3.32).
/// </summary>
public static class FindingDismissal
{
    public static async Task<bool> ConfirmAsync(IDialogService dialogs, ContinuityFinding finding) =>
        await dialogs.ShowMessageBoxAsync(
            "Dismiss this finding?",
            $"\"{DisplayText.Truncate(finding.Summary, 200)}\" will not be raised again, and the points it took off the score come back. This can't be undone.",
            yesText: "Dismiss", cancelText: "Keep it") == true;
}
