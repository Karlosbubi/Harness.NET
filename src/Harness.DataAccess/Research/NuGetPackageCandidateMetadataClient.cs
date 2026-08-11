using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace Harness.DataAccess.Research;

internal sealed class NuGetPackageCandidateMetadataClient(HttpClient httpClient)
    : IPackageCandidateMetadataClient
{
    private const long MaximumPackageBytes = 128 * 1024 * 1024;

    public async ValueTask<IReadOnlyList<PackageCandidateMetadata>> GetAsync(
        PackageCandidateQuery query,
        CancellationToken cancellationToken = default)
    {
        List<PackageCandidateMetadata> results = [];
        foreach (PackageSourceUri source in query.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ReadSourceAsync(query, source, cancellationToken));
        }
        return results;
    }

    private async ValueTask<PackageCandidateMetadata> ReadSourceAsync(
        PackageCandidateQuery query,
        PackageSourceUri source,
        CancellationToken cancellationToken)
    {
        if (source.Value.Scheme != Uri.UriSchemeHttps && !source.Value.IsLoopback)
        {
            return Failure(query, source, "insecure_package_source",
                "Package sources must use HTTPS except for loopback development servers.");
        }

        try
        {
            ServiceResources resources = await DiscoverAsync(source.Value, cancellationToken);
            string id = Uri.EscapeDataString(query.Package.Value.ToLowerInvariant());
            string version = Uri.EscapeDataString(query.Version.Value.ToLowerInvariant());
            Uri registrationUri = new(resources.Registration,
                $"{id}/{version}.json");
            using HttpResponseMessage registrationResponse = await httpClient.GetAsync(
                registrationUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (registrationResponse.StatusCode == HttpStatusCode.NotFound)
            {
                return Failure(query, source, "package_version_not_found",
                    "The exact package version is not present on this source.", registrationUri.AbsoluteUri);
            }
            registrationResponse.EnsureSuccessStatusCode();
            await using Stream registrationStream = await registrationResponse.Content
                .ReadAsStreamAsync(cancellationToken);
            using JsonDocument registration = await JsonDocument.ParseAsync(registrationStream,
                new JsonDocumentOptions { MaxDepth = 64 }, cancellationToken);
            JsonElement catalog = registration.RootElement.TryGetProperty("catalogEntry",
                out JsonElement embedded) && embedded.ValueKind == JsonValueKind.Object
                ? embedded
                : registration.RootElement;

            Uri packageUri = new(resources.PackageContent, $"{id}/{version}/{id}.{version}.nupkg");
            Uri hashUri = new(resources.PackageContent, $"{id}/{version}/{id}.{version}.nupkg.sha512");
            string? publishedHash = await TryReadTextAsync(hashUri, cancellationToken);
            PackageArchiveEvidence archive = await ReadPackageAsync(packageUri, query, cancellationToken);
            IReadOnlyList<PackageDependencyEdge> dependencies = ReadDependencies(catalog);
            IReadOnlyList<PackageAdvisory> advisories = ReadAdvisories(catalog);
            bool? deprecated = catalog.TryGetProperty("deprecation", out JsonElement deprecation) &&
                deprecation.ValueKind == JsonValueKind.Object
                ? true
                : null;
            string? deprecationMessage = deprecated == true
                ? StringValue(deprecation, "message")
                : null;
            string? repositoryUrl = null;
            string? repositoryCommit = null;
            if (catalog.TryGetProperty("repository", out JsonElement repository) &&
                repository.ValueKind == JsonValueKind.Object)
            {
                repositoryUrl = StringValue(repository, "url");
                repositoryCommit = StringValue(repository, "commit");
            }
            return new(
                query.Package,
                query.Version,
                source,
                Exists: true,
                BooleanValue(catalog, "listed"),
                IsPrerelease(query.Version.Value),
                deprecated,
                deprecationMessage,
                StringValue(catalog, "licenseExpression") ?? archive.LicenseExpression,
                UriValue(StringValue(catalog, "licenseUrl") ?? archive.LicenseUrl),
                UriValue(StringValue(catalog, "projectUrl") ?? archive.ProjectUrl),
                UriValue(repositoryUrl ?? archive.RepositoryUrl),
                repositoryCommit ?? archive.RepositoryCommit,
                NormalizeHash(publishedHash),
                archive.Sha512,
                dependencies.Count > 0 ? dependencies : archive.Dependencies,
                archive.Compatibility,
                archive.RuntimeCompatibility,
                advisories,
                registrationUri.AbsoluteUri,
                archive.ErrorCode,
                archive.Error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
            JsonException or InvalidDataException or NotSupportedException)
        {
            return Failure(query, source, "package_source_failed", exception.Message);
        }
    }

    private async ValueTask<ServiceResources> DiscoverAsync(
        Uri serviceIndex,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            serviceIndex, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream,
            new JsonDocumentOptions { MaxDepth = 32 }, cancellationToken);
        if (!document.RootElement.TryGetProperty("resources", out JsonElement resources) ||
            resources.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The NuGet service index has no resources array.");
        }

        Uri? registration = Resource(resources, "RegistrationsBaseUrl/3.6.0") ??
            Resource(resources, "RegistrationsBaseUrl");
        Uri? package = Resource(resources, "PackageBaseAddress/3.0.0");
        return registration is not null && package is not null
            ? new(EnsureTrailingSlash(registration), EnsureTrailingSlash(package))
            : throw new InvalidDataException(
                "The NuGet source does not publish registration and package-content resources.");
    }

    private async ValueTask<PackageArchiveEvidence> ReadPackageAsync(
        Uri uri,
        PackageCandidateQuery query,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumPackageBytes)
        {
            return PackageArchiveEvidence.Failure("package_archive_too_large",
                "The package archive exceeds the 128 MiB inspection limit.");
        }

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using MemoryStream buffer = new();
        byte[] block = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            int read = await source.ReadAsync(block, cancellationToken);
            if (read == 0)
            {
                break;
            }
            total += read;
            if (total > MaximumPackageBytes)
            {
                return PackageArchiveEvidence.Failure("package_archive_too_large",
                    "The package archive exceeds the 128 MiB inspection limit.");
            }
            await buffer.WriteAsync(block.AsMemory(0, read), cancellationToken);
        }

        string sha512 = Convert.ToBase64String(SHA512.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)));
        buffer.Position = 0;
        using ZipArchive archive = new(buffer, ZipArchiveMode.Read, leaveOpen: true);
        HashSet<string> frameworks = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> runtimes = new(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string[] parts = entry.FullName.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && parts[0] is "lib" or "ref")
            {
                frameworks.Add(parts[1]);
            }
            if (parts.Length >= 4 && parts[0] == "runtimes")
            {
                runtimes.Add(parts[1]);
                if (parts[2] is "lib" or "ref" && parts.Length >= 5)
                {
                    frameworks.Add(parts[3]);
                }
            }
        }

        PackageManifest manifest = await ReadManifestAsync(archive, cancellationToken);
        PackageAssetCompatibility[] compatibility = query.TargetFrameworks.Select(target =>
            new PackageAssetCompatibility(
                target,
                DetermineCompatibility(target.Value, frameworks),
                frameworks.Order(StringComparer.OrdinalIgnoreCase).ToArray())).ToArray();
        PackageRuntimeCompatibility[] runtimeCompatibility = query.RuntimeIdentifiers.Select(runtime =>
            new PackageRuntimeCompatibility(
                runtime,
                runtimes.Count == 0 ? true : runtimes.Contains(runtime.Value) ? true : null,
                runtimes.Order(StringComparer.OrdinalIgnoreCase).ToArray())).ToArray();
        return new(
            sha512,
            manifest.LicenseExpression,
            manifest.LicenseUrl,
            manifest.ProjectUrl,
            manifest.RepositoryUrl,
            manifest.RepositoryCommit,
            manifest.Dependencies,
            compatibility,
            runtimeCompatibility,
            null,
            null);
    }

    private static async ValueTask<PackageManifest> ReadManifestAsync(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(candidate =>
            candidate.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) &&
            !candidate.FullName.Contains('/', StringComparison.Ordinal));
        if (entry is null || entry.Length > 4 * 1024 * 1024)
        {
            return PackageManifest.Empty;
        }
        await using Stream stream = entry.Open();
        System.Xml.XmlReaderSettings xmlSettings = new()
        {
            Async = true,
            DtdProcessing = System.Xml.DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 4 * 1024 * 1024,
        };
        using System.Xml.XmlReader reader = System.Xml.XmlReader.Create(stream, xmlSettings);
        System.Xml.Linq.XDocument document = await System.Xml.Linq.XDocument.LoadAsync(
            reader, System.Xml.Linq.LoadOptions.None, cancellationToken);
        System.Xml.Linq.XElement? metadata = document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "metadata");
        if (metadata is null)
        {
            return PackageManifest.Empty;
        }
        System.Xml.Linq.XElement? repository = metadata.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "repository");
        string? licenseExpression = metadata.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "license" &&
                element.Attribute("type")?.Value == "expression")?.Value.Trim();
        return new(
            licenseExpression,
            ElementValue(metadata, "licenseUrl"),
            ElementValue(metadata, "projectUrl"),
            repository?.Attribute("url")?.Value,
            repository?.Attribute("commit")?.Value,
            metadata.Descendants().Where(element => element.Name.LocalName == "dependency")
                .Select(element => new PackageDependencyEdge(
                    new(element.Attribute("id")?.Value ?? string.Empty),
                    element.Attribute("version")?.Value ?? string.Empty))
                .Where(dependency => dependency.Package.Value.Length > 0)
                .Distinct()
                .OrderBy(dependency => dependency.Package.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static IReadOnlyList<PackageDependencyEdge> ReadDependencies(JsonElement catalog)
    {
        if (!catalog.TryGetProperty("dependencyGroups", out JsonElement groups) ||
            groups.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        return groups.EnumerateArray()
            .Where(group => group.TryGetProperty("dependencies", out JsonElement value) &&
                value.ValueKind == JsonValueKind.Array)
            .SelectMany(group => group.GetProperty("dependencies").EnumerateArray())
            .Select(dependency => new PackageDependencyEdge(
                new(StringValue(dependency, "id") ?? string.Empty),
                StringValue(dependency, "range") ?? string.Empty))
            .Where(dependency => dependency.Package.Value.Length > 0)
            .Distinct()
            .OrderBy(dependency => dependency.Package.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<PackageAdvisory> ReadAdvisories(JsonElement catalog)
    {
        if (!catalog.TryGetProperty("vulnerabilities", out JsonElement vulnerabilities) ||
            vulnerabilities.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        return vulnerabilities.EnumerateArray()
            .Select(item => new
            {
                Url = UriValue(StringValue(item, "advisoryUrl")),
                Severity = IntValue(item, "severity"),
            })
            .Where(item => item.Url is not null)
            .Select(item => new PackageAdvisory(item.Url!, item.Severity))
            .OrderBy(item => item.Url.AbsoluteUri, StringComparer.Ordinal)
            .ToArray();
    }

    private async ValueTask<string?> TryReadTextAsync(Uri uri, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadAsStringAsync(cancellationToken)
            : null;
    }

    private static Uri? Resource(JsonElement resources, string type)
    {
        foreach (JsonElement resource in resources.EnumerateArray())
        {
            string? actual = StringValue(resource, "@type");
            string? id = StringValue(resource, "@id");
            if (actual is not null && id is not null &&
                actual.Split(';')[0].Equals(type, StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(id, UriKind.Absolute, out Uri? uri))
            {
                return uri;
            }
        }
        return null;
    }

    private static bool? DetermineCompatibility(string target, IReadOnlySet<string> groups)
    {
        if (groups.Count == 0)
        {
            return true;
        }
        if (groups.Contains(target) || groups.Contains("any"))
        {
            return true;
        }
        if (target.StartsWith("net", StringComparison.OrdinalIgnoreCase) &&
            groups.Any(group => group.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        return null;
    }

    private static string? NormalizeHash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('"');

    private static Uri EnsureTrailingSlash(Uri value) => value.AbsoluteUri.EndsWith("/",
        StringComparison.Ordinal) ? value : new(value.AbsoluteUri + "/");

    private static bool IsPrerelease(string version) => version.Contains('-', StringComparison.Ordinal);

    private static string? StringValue(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? BooleanValue(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static int IntValue(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : 0;

    private static Uri? UriValue(string? value) => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
        ? uri
        : null;

    private static string? ElementValue(System.Xml.Linq.XContainer parent, string name) =>
        parent.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim();

    private static PackageCandidateMetadata Failure(
        PackageCandidateQuery query,
        PackageSourceUri source,
        string code,
        string error,
        string? citation = null) => new(
        query.Package,
        query.Version,
        source,
        Exists: false,
        IsListed: null,
        IsPrerelease(query.Version.Value),
        IsDeprecated: null,
        DeprecationMessage: null,
        LicenseExpression: null,
        LicenseUrl: null,
        ProjectUrl: null,
        RepositoryUrl: null,
        RepositoryCommit: null,
        PublishedSha512: null,
        ComputedSha512: null,
        Dependencies: [],
        Compatibility: [],
        RuntimeCompatibility: [],
        Advisories: [],
        citation ?? source.Value.AbsoluteUri,
        code,
        error);

    private sealed record ServiceResources(Uri Registration, Uri PackageContent);

    private sealed record PackageArchiveEvidence(
        string? Sha512,
        string? LicenseExpression,
        string? LicenseUrl,
        string? ProjectUrl,
        string? RepositoryUrl,
        string? RepositoryCommit,
        IReadOnlyList<PackageDependencyEdge> Dependencies,
        IReadOnlyList<PackageAssetCompatibility> Compatibility,
        IReadOnlyList<PackageRuntimeCompatibility> RuntimeCompatibility,
        string? ErrorCode,
        string? Error)
    {
        internal static PackageArchiveEvidence Failure(string code, string error) =>
            new(null, null, null, null, null, null, [], [], [], code, error);
    }

    private sealed record PackageManifest(
        string? LicenseExpression,
        string? LicenseUrl,
        string? ProjectUrl,
        string? RepositoryUrl,
        string? RepositoryCommit,
        IReadOnlyList<PackageDependencyEdge> Dependencies)
    {
        internal static PackageManifest Empty { get; } = new(null, null, null, null, null, []);
    }
}
