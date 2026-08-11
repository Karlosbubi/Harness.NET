using Harness.DataAccess.Research;

namespace Harness.BusinessLogic.Research;

internal sealed class DocumentationResearchService(
    ResearchWorkspaceResolver workspaceResolver,
    IEnumerable<IDocumentationSource> sources,
    IResearchSettingsStore settingsStore,
    IDocumentationCache cache,
    TimeProvider timeProvider,
    IDependencyResearchService? dependencyResearchService = null) : IDocumentationResearchService
{
    private const string AdapterSchemaVersion = "documentation-evidence-v1";

    public async ValueTask<DocumentationLookupResult> LookupAsync(
        DocumentationLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        string? validation = Validate(request);
        if (validation is not null)
        {
            return Failure(request, "invalid_documentation_lookup", validation);
        }
        if (request.GoalId is not null && await workspaceResolver.ResolveAsync(
                request.GoalId,
                DependencyInspectionScope.Original,
                cancellationToken) is null)
        {
            return Failure(request, "goal_workspace_unavailable",
                "The trusted goal workspace is unavailable.");
        }

        ResearchLibraryVersion? effectiveVersion = request.Version;
        ResearchEscalationView? versionResolution = null;
        if (effectiveVersion is null && dependencyResearchService is not null)
        {
            DependencyInspectionResult dependencies = await dependencyResearchService.InspectAsync(
                new(request.GoalId, DependencyInspectionScope.Original), cancellationToken);
            DocumentationLibraryCatalogEntry? catalog = DocumentationLibraryCatalog.Core.Entries
                .FirstOrDefault(item => item.Name.Value.Equals(request.Library.Value,
                    StringComparison.OrdinalIgnoreCase));
            string[] versions = catalog is null ? [] : dependencies.Projects
                .SelectMany(project => project.Packages)
                .Where(package => catalog.PackageIds.Contains(package.Package.Value,
                    StringComparer.OrdinalIgnoreCase))
                .Select(package => package.ResolvedVersion?.Value ?? package.CentralVersion?.Value ??
                    package.DeclaredVersion?.Value)
                .Where(version => !string.IsNullOrWhiteSpace(version))
                .Select(version => version!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (versions.Length == 0 && catalog?.Name.Value.Equals(".NET",
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                versions = dependencies.Projects
                    .SelectMany(project => project.TargetFrameworks)
                    .Select(target => DotNetDocumentationVersion(target.Value))
                    .Where(version => version is not null)
                    .Select(version => version!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            if (versions.Length == 1)
            {
                effectiveVersion = new(versions[0]);
                versionResolution = new(new("workspace-dependency-evidence"),
                    ResearchSourceKind.ExactLocal, ResearchEscalationAction.Sufficient,
                    $"Resolved {request.Library.Value} version {versions[0]} from the current dependency graph.");
            }
            else if (versions.Length > 1)
            {
                versionResolution = new(new("workspace-dependency-evidence"),
                    ResearchSourceKind.ExactLocal, ResearchEscalationAction.Insufficient,
                    $"The dependency graph contains conflicting versions: {string.Join(", ", versions)}.");
            }
        }
        ResearchSourceSettings settings = await settingsStore.GetAsync(cancellationToken);
        DocumentationSourceQuery query = new(
            new(request.Library.Value.Trim()),
            effectiveVersion is null ? null : new(effectiveVersion.Value.Trim()),
            new(request.Question.Value.Trim()),
            settings.MaximumResults,
            settings.MaximumCharacters);
        List<DocumentationSourceMatch> accumulated = [];
        List<ResearchEscalationView> escalation = [];
        if (versionResolution is not null)
        {
            escalation.Add(versionResolution);
        }
        bool sufficient = false;

        foreach (IDocumentationSource source in sources.OrderBy(source => source.SourceClass))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Configured(source.SourceClass, settings))
            {
                escalation.Add(Escalation(source, ResearchEscalationAction.Skipped,
                    "This source class is disabled in Settings."));
                continue;
            }

            if (settings.Offline && source.SourceClass is DocumentationSourceClass.Mcp or
                DocumentationSourceClass.Web)
            {
                escalation.Add(Escalation(source, ResearchEscalationAction.Skipped,
                    "Offline mode blocks live network documentation lookup."));
                DocumentationSourceResult? offlineCache = await AddCachedAsync(
                    source, query, settings, accumulated, escalation,
                    allowStale: true, cancellationToken);
                if (offlineCache?.IsSufficient == true &&
                    offlineCache.Matches.All(match => !match.IsStale))
                {
                    sufficient = true;
                    escalation.Add(Escalation(source, ResearchEscalationAction.Sufficient,
                        "A fresh cached result satisfies the offline query."));
                    break;
                }
                continue;
            }

            DocumentationSourceResult? cached = await AddCachedAsync(
                source, query, settings, accumulated, escalation,
                allowStale: settings.Offline, cancellationToken);
            if (cached?.IsSufficient == true && cached.Matches.All(match => !match.IsStale))
            {
                sufficient = true;
                escalation.Add(Escalation(source, ResearchEscalationAction.Sufficient,
                    "A fresh cached result satisfies the versioned query."));
                break;
            }
            escalation.Add(Escalation(source, ResearchEscalationAction.Queried,
                "Earlier authoritative evidence was insufficient."));
            DocumentationSourceResult result;
            try
            {
                result = await source.SearchAsync(query, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                InvalidDataException or HttpRequestException)
            {
                escalation.Add(Escalation(source, ResearchEscalationAction.Failed,
                    exception.Message));
                continue;
            }
            accumulated.AddRange(result.Matches);
            if (result.ErrorCode is not null)
            {
                escalation.Add(Escalation(source, ResearchEscalationAction.Failed,
                    result.Error ?? result.ErrorCode));
            }
            try
            {
                await cache.PutAsync(new(
                    Key(source, query),
                    result,
                    timeProvider.GetUtcNow()), cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                InvalidDataException or NotSupportedException)
            {
                escalation.Add(Escalation(source, ResearchEscalationAction.Failed,
                    $"The result was usable but could not be cached: {exception.Message}"));
            }
            if (result.IsSufficient)
            {
                sufficient = true;
                escalation.Add(Escalation(source, ResearchEscalationAction.Sufficient,
                    "This source returned enough bounded evidence."));
                break;
            }
            escalation.Add(Escalation(source, ResearchEscalationAction.Insufficient,
                result.Matches.Count == 0
                    ? "The source returned no matching evidence."
                    : "The evidence was not an exact or confident enough version match."));
        }

        bool conflicts = accumulated.GroupBy(match =>
                (Normalize(match.Title), match.Version?.Value.ToLowerInvariant() ?? string.Empty))
            .Any(group => group.Select(match => match.ContentSha256)
                .Distinct(StringComparer.Ordinal).Count() > 1);
        DocumentationSourceMatch[] ranked = Rank(
            accumulated, settings.MaximumResults, settings.MaximumCharacters);
        DocumentationEvidenceView[] views = ranked.Select((match, index) => new DocumentationEvidenceView(
            new(match.Source.Value),
            Map(match.SourceClass),
            match.Title,
            match.Content,
            match.Version is null ? null : new(match.Version.Value),
            match.IsStale ? ResearchFreshness.Stale : ResearchFreshness.Fresh,
            match.Confidence >= 0.75m ? ResearchConfidence.High :
                match.Confidence >= 0.5m ? ResearchConfidence.Medium : ResearchConfidence.Low,
            new(match.Citation.Value),
            match.RetrievedAt,
            index + 1,
            match.IsExactVersion)).ToArray();
        return new(
            request.Library,
            request.Version,
            views,
            escalation,
            sufficient,
            conflicts,
            views.Length == 0 ? "documentation_unavailable" : null,
            views.Length == 0
                ? settings.Offline
                    ? "No matching local or cached documentation is available while offline."
                    : "No configured source returned matching documentation."
                : null);
    }

    private async ValueTask<DocumentationSourceResult?> AddCachedAsync(
        IDocumentationSource source,
        DocumentationSourceQuery query,
        ResearchSourceSettings settings,
        ICollection<DocumentationSourceMatch> accumulated,
        ICollection<ResearchEscalationView> escalation,
        bool allowStale,
        CancellationToken cancellationToken)
    {
        DocumentationCacheEntry? entry = await cache.GetAsync(Key(source, query), cancellationToken);
        if (entry is null)
        {
            return null;
        }
        TimeSpan refreshAge = settings.RefreshPolicy switch
        {
            ResearchRefreshPolicy.Daily => settings.MaximumCacheAge < TimeSpan.FromDays(1)
                ? settings.MaximumCacheAge : TimeSpan.FromDays(1),
            ResearchRefreshPolicy.Weekly => settings.MaximumCacheAge < TimeSpan.FromDays(7)
                ? settings.MaximumCacheAge : TimeSpan.FromDays(7),
            ResearchRefreshPolicy.OnDemand or ResearchRefreshPolicy.Manual => settings.MaximumCacheAge,
            _ => settings.MaximumCacheAge,
        };
        bool stale = timeProvider.GetUtcNow() - entry.StoredAt > refreshAge;
        if (stale && !allowStale)
        {
            escalation.Add(Escalation(source, ResearchEscalationAction.Insufficient,
                "The cached result is stale; refreshing this source."));
            return entry.Result;
        }
        DocumentationSourceMatch[] matches = entry.Result.Matches
            .Select(match => match with { IsStale = stale || match.IsStale })
            .ToArray();
        foreach (DocumentationSourceMatch match in matches)
        {
            accumulated.Add(match);
        }
        escalation.Add(Escalation(source, ResearchEscalationAction.CacheHit,
            stale ? "Using stale cached evidence because live lookup is unavailable."
                : "Using a fresh cache entry with the same source, version, query, and schema identity."));
        return entry.Result with { Matches = matches };
    }

    private static DocumentationSourceMatch[] Rank(
        IEnumerable<DocumentationSourceMatch> values,
        int maximumResults,
        int maximumCharacters)
    {
        List<DocumentationSourceMatch> output = [];
        int characters = 0;
        foreach (DocumentationSourceMatch match in values
                     .GroupBy(value => (Normalize(value.Citation.Value),
                         value.Version?.Value.ToLowerInvariant() ?? string.Empty,
                         value.ContentSha256))
                     .Select(group => group.OrderBy(item => item.SourceClass)
                         .ThenByDescending(item => item.Confidence).First())
                     .OrderByDescending(item => item.IsExactVersion)
                     .ThenBy(item => item.SourceClass)
                     .ThenByDescending(item => item.Confidence)
                     .ThenBy(item => item.Citation.Value, StringComparer.Ordinal))
        {
            if (output.Count >= maximumResults || characters >= maximumCharacters)
            {
                break;
            }
            int remaining = maximumCharacters - characters;
            string content = match.Content.Length <= remaining ? match.Content : match.Content[..remaining];
            if (content.Length == 0)
            {
                break;
            }
            output.Add(match with { Content = content, ContentSha256 = Hash(content) });
            characters += content.Length;
        }
        return output.ToArray();
    }

    private static bool Configured(DocumentationSourceClass source, ResearchSourceSettings settings) =>
        source switch
        {
            DocumentationSourceClass.ExactLocal => settings.ExactLocalEnabled,
            DocumentationSourceClass.LocalIndex => settings.LocalIndexEnabled,
            DocumentationSourceClass.Mcp => settings.McpEnabled,
            DocumentationSourceClass.Web => settings.WebEnabled,
            _ => false,
        };

    private static DocumentationCacheKey Key(
        IDocumentationSource source,
        DocumentationSourceQuery query) => new(
        source.Id,
        query.Library,
        query.Version,
        query.Query,
        AdapterSchemaVersion,
        DocumentationDisclosureClass.PublicResearchTerms);

    private static ResearchEscalationView Escalation(
        IDocumentationSource source,
        ResearchEscalationAction action,
        string reason) => new(new(source.Id.Value), Map(source.SourceClass), action, reason);

    private static ResearchSourceKind Map(DocumentationSourceClass value) => value switch
    {
        DocumentationSourceClass.ExactLocal => ResearchSourceKind.ExactLocal,
        DocumentationSourceClass.LocalIndex => ResearchSourceKind.LocalIndex,
        DocumentationSourceClass.Mcp => ResearchSourceKind.Mcp,
        DocumentationSourceClass.Web => ResearchSourceKind.Web,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static string? DotNetDocumentationVersion(string targetFramework)
    {
        string value = targetFramework.Trim().Split('-', 2)[0];
        string prefix = value.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)
            ? "netstandard"
            : value.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase)
                ? "netcoreapp"
            : value.StartsWith("net", StringComparison.OrdinalIgnoreCase) ? "net" : string.Empty;
        string versionText = prefix.Length == 0 ? string.Empty : value[prefix.Length..];
        if (!versionText.Contains('.', StringComparison.Ordinal) ||
            !Version.TryParse(versionText, out Version? version))
        {
            return null;
        }
        return $"{version.Major}.{version.Minor}";
    }

    private static string Hash(string value) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))
        .ToLowerInvariant();

    private static string? Validate(DocumentationLookupRequest request)
    {
        if (request?.Library is null || request.Question is null ||
            string.IsNullOrWhiteSpace(request.Library.Value) || request.Library.Value.Length > 200 ||
            string.IsNullOrWhiteSpace(request.Question.Value) || request.Question.Value.Length > 2_000 ||
            request.Version?.Value.Length > 100)
        {
            return "A library of 1-200 characters, optional version up to 100 characters, and question of 1-2000 characters are required.";
        }
        return null;
    }

    private static DocumentationLookupResult Failure(
        DocumentationLookupRequest request,
        string code,
        string error) => new(
        request.Library,
        request.Version,
        [],
        [],
        false,
        false,
        code,
        error);
}
