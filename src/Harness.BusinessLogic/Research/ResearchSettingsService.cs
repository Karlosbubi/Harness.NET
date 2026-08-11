using Harness.DataAccess.Research;
using System.Xml;

namespace Harness.BusinessLogic.Research;

internal sealed class ResearchSettingsService(
    IResearchSettingsStore settingsStore,
    IDocumentationCache cache,
    TimeProvider timeProvider) : IResearchSettingsService
{
    public async ValueTask<ResearchSettingsSnapshot> GetAsync(
        CancellationToken cancellationToken = default) =>
        Map(await settingsStore.GetAsync(cancellationToken),
            await cache.GetStatusAsync(cancellationToken));

    public async ValueTask<ResearchSettingsResult> SaveAsync(
        ResearchSettingsUpdate update,
        CancellationToken cancellationToken = default)
    {
        string? validation = Validate(update, out ResearchSourceSettings? settings);
        if (validation is not null || settings is null)
        {
            return new(null, "invalid_research_settings", validation);
        }
        try
        {
            await settingsStore.SaveAsync(settings, cancellationToken);
            return new(await GetAsync(cancellationToken), null, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidDataException or XmlException)
        {
            return new(null, "research_settings_save_failed", exception.Message);
        }
    }

    public async ValueTask<ResearchSettingsSnapshot> CleanupCacheAsync(
        CancellationToken cancellationToken = default)
    {
        ResearchSourceSettings settings = await settingsStore.GetAsync(cancellationToken);
        DocumentationCacheStatus status = await cache.CleanupAsync(
            timeProvider.GetUtcNow() - settings.Retention, cancellationToken);
        return Map(settings, status);
    }

    private static ResearchSettingsSnapshot Map(
        ResearchSourceSettings settings,
        DocumentationCacheStatus cache) => new(
        settings.ExactLocalEnabled,
        settings.LocalIndexEnabled,
        settings.McpEnabled,
        settings.WebEnabled,
        settings.Offline,
        settings.IndexRoots.Select(root => root.Value).ToArray(),
        settings.McpTools.Select(route => $"{route.Connection}/{route.Tool}").ToArray(),
        settings.WebEndpoints.Select(endpoint => endpoint.Value.AbsoluteUri).ToArray(),
        settings.PackageSources.Select(source => source.Value.AbsoluteUri).ToArray(),
        settings.RefreshPolicy switch
        {
            ResearchRefreshPolicy.OnDemand => ResearchRefreshMode.OnDemand,
            ResearchRefreshPolicy.Daily => ResearchRefreshMode.Daily,
            ResearchRefreshPolicy.Weekly => ResearchRefreshMode.Weekly,
            ResearchRefreshPolicy.Manual => ResearchRefreshMode.Manual,
            _ => throw new ArgumentOutOfRangeException(nameof(settings)),
        },
        settings.MaximumResults,
        settings.MaximumCharacters,
        (int)settings.MaximumCacheAge.TotalHours,
        (int)settings.Retention.TotalDays,
        cache.EntryCount,
        cache.SizeBytes,
        cache.LastFailure);

    private static string? Validate(
        ResearchSettingsUpdate update,
        out ResearchSourceSettings? settings)
    {
        settings = null;
        if (update is null || update.IndexRoots.Count > 20 ||
            update.McpDocumentationTools.Count > 50 || update.WebEndpoints.Count > 20 ||
            update.PackageSources.Count is < 1 or > 20 || update.MaximumResults is < 1 or > 20 ||
            update.MaximumCharacters is < 1_000 or > 100_000 ||
            update.MaximumCacheAgeHours is < 0 or > 8_760 ||
            update.RetentionDays is < 0 or > 3_650)
        {
            return "Research limits or source counts are outside their allowed ranges.";
        }
        string[] roots;
        try
        {
            roots = update.IndexRoots.Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return $"A documentation index path is invalid: {exception.Message}";
        }
        List<DocumentationMcpToolRoute> routes = [];
        foreach (string value in update.McpDocumentationTools.Select(value => value.Trim())
                     .Where(value => value.Length > 0))
        {
            int separator = value.IndexOf('/');
            if (separator is <= 0 || separator == value.Length - 1)
            {
                return "Each MCP documentation tool must use connection/tool form.";
            }
            routes.Add(new(value[..separator], value[(separator + 1)..]));
        }
        if (!TryUris(update.WebEndpoints, allowEmpty: true, out Uri[] web, out string? uriError) ||
            !TryUris(update.PackageSources, allowEmpty: false, out Uri[] package, out uriError))
        {
            return uriError;
        }
        settings = new(
            update.ExactLocalEnabled,
            update.LocalIndexEnabled,
            update.McpEnabled,
            update.WebEnabled,
            update.Offline,
            roots.Select(root => new DocumentationIndexRoot(root)).ToArray(),
            routes,
            web.Select(uri => new DocumentationWebEndpoint(uri)).ToArray(),
            package.Select(uri => new PackageSourceUri(uri)).ToArray(),
            update.RefreshMode switch
            {
                ResearchRefreshMode.OnDemand => ResearchRefreshPolicy.OnDemand,
                ResearchRefreshMode.Daily => ResearchRefreshPolicy.Daily,
                ResearchRefreshMode.Weekly => ResearchRefreshPolicy.Weekly,
                ResearchRefreshMode.Manual => ResearchRefreshPolicy.Manual,
                _ => throw new ArgumentOutOfRangeException(nameof(update)),
            },
            update.MaximumResults,
            update.MaximumCharacters,
            TimeSpan.FromHours(update.MaximumCacheAgeHours),
            TimeSpan.FromDays(update.RetentionDays));
        return null;
    }

    private static bool TryUris(
        IReadOnlyList<string> values,
        bool allowEmpty,
        out Uri[] uris,
        out string? error)
    {
        List<Uri> parsed = [];
        foreach (string value in values.Select(value => value.Trim()).Where(value => value.Length > 0))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
                uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
            {
                uris = [];
                error = "Research endpoints must be absolute HTTPS URIs; HTTP is allowed only for loopback.";
                return false;
            }
            parsed.Add(uri);
        }
        if (!allowEmpty && parsed.Count == 0)
        {
            uris = [];
            error = "At least one package source is required.";
            return false;
        }
        uris = parsed.DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase).ToArray();
        error = null;
        return true;
    }
}
