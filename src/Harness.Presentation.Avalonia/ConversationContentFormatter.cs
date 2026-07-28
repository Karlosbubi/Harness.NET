using System.Net;
using System.Text.RegularExpressions;
using Markdig;

namespace Harness.Presentation.Avalonia;

internal static partial class ConversationContentFormatter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAdvancedExtensions()
        .Build();

    internal static MarkdownPipeline MarkdownPipeline => Pipeline;

    internal static string NormalizeSource(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        string text = WebUtility.HtmlDecode(content)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase);
        return ProviderHtmlArtifact().Replace(text, string.Empty);
    }

    internal static string ToReadableText(string content)
    {
        string source = NormalizeSource(content);
        if (source.Length == 0)
        {
            return string.Empty;
        }

        string text = Markdown.ToPlainText(source, Pipeline);
        return ExcessBlankLines().Replace(text.Trim(), "\n\n");
    }

    [GeneratedRegex("</?(?:blockquote|p|div|span|strong|em)>", RegexOptions.IgnoreCase)]
    private static partial Regex ProviderHtmlArtifact();

    [GeneratedRegex("\\n{3,}")]
    private static partial Regex ExcessBlankLines();
}
