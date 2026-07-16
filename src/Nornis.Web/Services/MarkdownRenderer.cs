using Markdig;
using Microsoft.AspNetCore.Components;

namespace Nornis.Web.Services;

/// <summary>
/// Renders stored source bodies (markdown from the capture editor, or legacy plain text) to HTML
/// for display. Raw HTML in the input is disabled — user text can never smuggle markup — and
/// single newlines render as line breaks so legacy plain-text bodies keep their current look.
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseSoftlineBreakAsHardlineBreak()
        .UsePipeTables()
        .UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough)
        .Build();

    public static MarkupString ToHtml(string? markdown) =>
        string.IsNullOrWhiteSpace(markdown)
            ? new MarkupString(string.Empty)
            : new MarkupString(Markdown.ToHtml(markdown, Pipeline));
}
