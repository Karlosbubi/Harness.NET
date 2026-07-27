using Harness.DataAccess.Framework;

namespace Harness.BusinessLogic.Framework;

internal sealed class FrameworkService(
    IFrameworkSourceReader sourceReader,
    IFrameworkOverlayStore overlayStore,
    IFrameworkResolver resolver,
    FrameworkOptions options) : IFrameworkService
{
    public async ValueTask<FrameworkSnapshot> GetEffectiveAsync(
        string workspaceId,
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        FrameworkSourceResult sources = await sourceReader.ReadAsync(
            workspaceRoot,
            cancellationToken);
        WorkspaceFrameworkOverlay? overlay = await overlayStore.GetAsync(
            workspaceId,
            cancellationToken);
        FrameworkDocumentView[] documents = sources.Documents
            .Select(ToView)
            .AppendIfNotNull(overlay is null
                ? null
                : new(
                    "private-workspace",
                    2,
                    "Harness.NET private overlay",
                    overlay.Content,
                    IsPrivate: true))
            .OrderBy(document => document.Precedence)
            .ThenBy(document => document.Source, StringComparer.Ordinal)
            .ToArray();
        FrameworkResolution resolution = resolver.Resolve(options.Rules);
        FrameworkIssue[] sourceIssues = sources.Errors
            .Select(error => new FrameworkIssue(
                "source_error",
                error,
                Key: null,
                Sources: []))
            .ToArray();
        return new(documents, resolution.Rules, [.. resolution.Issues, .. sourceIssues]);
    }

    public async ValueTask<FrameworkSnapshot> SetPrivateOverlayAsync(
        string workspaceId,
        string workspaceRoot,
        string? content,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            await overlayStore.DeleteAsync(workspaceId, cancellationToken);
        }
        else
        {
            await overlayStore.SaveAsync(workspaceId, content.Trim(), cancellationToken);
        }

        return await GetEffectiveAsync(workspaceId, workspaceRoot, cancellationToken);
    }

    private static FrameworkDocumentView ToView(FrameworkDocument document) => new(
        document.Layer,
        document.Precedence,
        document.Source,
        document.Content,
        document.IsPrivate);
}

internal static class FrameworkDocumentEnumerableExtensions
{
    internal static IEnumerable<T> AppendIfNotNull<T>(
        this IEnumerable<T> source,
        T? value)
        where T : class =>
        value is null ? source : source.Append(value);
}
