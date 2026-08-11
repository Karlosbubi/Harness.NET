using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Harness.DataAccess.Research;

namespace Harness.BusinessLogic.Research;

internal sealed class DependencyResearchService(
    ResearchWorkspaceResolver workspaceResolver,
    IDependencyEvidenceReader evidenceReader,
    IPackageCandidateMetadataClient metadataClient,
    IResearchSettingsStore settingsStore,
    ISbomExporter sbomExporter) : IDependencyResearchService
{
    public async ValueTask<DependencyInspectionResult> InspectAsync(
        DependencyInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ResearchWorkspaceContext? workspace = await workspaceResolver.ResolveAsync(
            request.GoalId, request.Scope, cancellationToken);
        if (workspace is null)
        {
            return InspectionFailure("goal_workspace_unavailable",
                "The trusted workspace or requested goal worktree is unavailable.");
        }
        DependencyEvidenceSnapshot snapshot = await evidenceReader.InspectAsync(
            workspace.RootPath, workspace.EntryPoint, cancellationToken);
        return Map(snapshot);
    }

    public async ValueTask<PackageCandidateValidationResult> ValidateCandidateAsync(
        PackageCandidateValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request?.Package is null || request.Version is null ||
            string.IsNullOrWhiteSpace(request.Package.Value) || request.Package.Value.Length > 200 ||
            string.IsNullOrWhiteSpace(request.Version.Value) || request.Version.Value.Length > 100)
        {
            return CandidateFailure(request?.Package ?? new(string.Empty),
                request?.Version ?? new(string.Empty), "invalid_package_candidate",
                "A package ID of 1-200 characters and exact version of 1-100 characters are required.");
        }
        DependencyInspectionResult inspection = await InspectAsync(
            new(request.GoalId, request.Scope), cancellationToken);
        if (inspection.ErrorCode is not null)
        {
            return CandidateFailure(request.Package, request.Version, inspection.ErrorCode,
                inspection.Error ?? "Dependency inspection failed.");
        }
        ResearchSourceSettings settings = await settingsStore.GetAsync(cancellationToken);
        if (settings.Offline)
        {
            return CandidateFailure(request.Package, request.Version, "offline_package_validation",
                "Exact candidate validation requires a configured package source and is disabled offline.");
        }
        PackageCandidateQuery query = new(
            new(request.Package.Value),
            new(request.Version.Value),
            inspection.Projects.SelectMany(project => project.TargetFrameworks)
                .Select(target => new TargetFrameworkMoniker(target.Value))
                .Distinct().ToArray(),
            inspection.Projects.SelectMany(project => project.RuntimeIdentifiers)
                .Select(runtime => new RuntimeIdentifier(runtime.Value))
                .Distinct().ToArray(),
            request.AllowPrerelease,
            settings.PackageSources);
        IReadOnlyList<PackageCandidateMetadata> metadata = await metadataClient.GetAsync(
            query, cancellationToken);
        List<string> findings = [];
        bool rejected = false;
        bool review = false;
        PackageCandidateMetadata[] available = metadata.Where(item => item.Exists).ToArray();
        if (available.Length == 0)
        {
            rejected = true;
            findings.Add("The exact package and version is unavailable from every configured source.");
        }
        if (request.Version.Value.Contains('-', StringComparison.Ordinal) && !request.AllowPrerelease)
        {
            rejected = true;
            findings.Add("The candidate is a prerelease and the request does not allow prerelease packages.");
        }
        foreach (PackageCandidateMetadata source in available)
        {
            if (source.IsListed == false)
            {
                review = true;
                findings.Add($"{source.Source.Value.Host} reports the version as unlisted.");
            }
            else if (source.IsListed is null)
            {
                review = true;
                findings.Add($"{source.Source.Value.Host} did not provide listing-state evidence.");
            }
            if (source.IsDeprecated == true)
            {
                review = true;
                findings.Add($"{source.Source.Value.Host} reports the version as deprecated: " +
                    (source.DeprecationMessage ?? "no message supplied"));
            }
            else if (source.IsDeprecated is null)
            {
                review = true;
                findings.Add($"{source.Source.Value.Host} did not provide deprecation-state evidence.");
            }
            if (source.Advisories.Count > 0)
            {
                rejected = true;
                findings.Add($"{source.Source.Value.Host} reports {source.Advisories.Count} advisory item(s).");
            }
            else
            {
                review = true;
                findings.Add($"{source.Source.Value.Host} reports no advisory items; registry silence " +
                    "is not proof that no advisory exists.");
            }
            if (source.Compatibility.Any(item => item.IsCompatible == false))
            {
                rejected = true;
                findings.Add($"{source.Source.Value.Host} has a known incompatible target-framework asset set.");
            }
            if (source.Compatibility.Any(item => item.IsCompatible is null))
            {
                review = true;
                findings.Add($"{source.Source.Value.Host} could not prove compatibility for every target framework.");
            }
            if (source.RuntimeCompatibility.Any(item => item.IsCompatible == false))
            {
                rejected = true;
                findings.Add($"{source.Source.Value.Host} has a known incompatible runtime asset set.");
            }
            if (source.RuntimeCompatibility.Any(item => item.IsCompatible is null))
            {
                review = true;
                findings.Add($"{source.Source.Value.Host} could not prove compatibility for every runtime identifier.");
            }
            if (source.PublishedSha512 is not null && source.ComputedSha512 is not null &&
                !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(source.PublishedSha512),
                    Encoding.UTF8.GetBytes(source.ComputedSha512)))
            {
                rejected = true;
                findings.Add($"{source.Source.Value.Host} package content does not match its published SHA-512.");
            }
            if (source.PublishedSha512 is null || source.ComputedSha512 is null)
            {
                review = true;
                findings.Add($"{source.Source.Value.Host} did not provide both published and computed integrity evidence.");
            }
            if (source.LicenseExpression is null && source.LicenseUrl is null)
            {
                review = true;
                findings.Add($"{source.Source.Value.Host} has no machine-readable license evidence.");
            }
            if (source.RepositoryUrl is null)
            {
                review = true;
                findings.Add($"{source.Source.Value.Host} has no repository provenance URL.");
            }
        }
        if (metadata.Any(item => item.ErrorCode is not null))
        {
            review = true;
            findings.Add("At least one configured package source failed or returned incomplete evidence.");
        }
        if (available.Select(item => item.ComputedSha512).Where(value => value is not null)
                .Distinct(StringComparer.Ordinal).Count() > 1)
        {
            rejected = true;
            findings.Add("Configured sources returned different package archive hashes.");
        }
        PackageCandidateDecision decision = rejected ? PackageCandidateDecision.Rejected :
            review ? PackageCandidateDecision.ReviewRequired : PackageCandidateDecision.Accepted;
        if (findings.Count == 0)
        {
            findings.Add("The configured source evidence satisfies the deterministic candidate policy.");
        }
        return new(
            request.Package,
            request.Version,
            decision,
            findings.Distinct(StringComparer.Ordinal).ToArray(),
            metadata.Select(Map).ToArray(),
            null,
            null);
    }

    public async ValueTask<SbomPreviewResult> PreviewSbomAsync(
        SbomPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        DependencyInspectionResult dependencies = await InspectAsync(
            new(request.GoalId, request.Scope), cancellationToken);
        if (dependencies.ErrorCode is not null)
        {
            return new(dependencies, null, dependencies.ErrorCode, dependencies.Error);
        }
        if (dependencies.Projects.Any(project => !project.HasRestoredAssets))
        {
            return new(dependencies, null, "restored_graph_incomplete",
                "Every project must have an existing project.assets.json before an SBOM can be reproduced.");
        }
        SbomDocument sbom = GenerateSbom(ComponentSpecs(dependencies));
        return new(dependencies, sbom, null, null);
    }

    public async ValueTask<PackageChangePreviewResult> PreviewPackageChangeAsync(
        PackageChangePreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        PackageCandidateValidationResult validation = await ValidateCandidateAsync(new(
            request.GoalId,
            request.Package,
            request.Version,
            request.AllowPrerelease,
            request.Scope), cancellationToken);
        SbomPreviewResult current = await PreviewSbomAsync(
            new(request.GoalId, request.Scope), cancellationToken);
        if (validation.ErrorCode is not null || current.Sbom is null)
        {
            return new(validation, string.Empty, string.Empty, current.Sbom, null,
                validation.ErrorCode ?? current.ErrorCode,
                validation.Error ?? current.Error);
        }
        List<ComponentSpec> proposed = ComponentSpecs(current.Dependencies).ToList();
        proposed.RemoveAll(component => component.Id.Equals(
            request.Package.Value, StringComparison.OrdinalIgnoreCase));
        PackageSourceEvidenceView? source = validation.Sources.FirstOrDefault(item => item.Exists);
        proposed.Add(new(
            request.Package.Value,
            request.Version.Value,
            source?.ComputedSha512,
            source?.License,
            source?.RepositoryUrl,
            IsDirect: true,
            Evidence: [source?.Citation.Value ?? "candidate validation"],
            Dependencies: source?.Dependencies ?? []));
        SbomDocument proposedSbom = GenerateSbom(proposed);
        string? currentVersion = current.Dependencies.Projects.SelectMany(project => project.Packages)
            .Where(package => package.Package.Value.Equals(request.Package.Value,
                StringComparison.OrdinalIgnoreCase))
            .Select(package => package.ResolvedVersion?.Value)
            .FirstOrDefault(value => value is not null);
        StringBuilder dependencyDiff = new();
        dependencyDiff.AppendLine("--- current dependencies");
        dependencyDiff.AppendLine("+++ proposed dependencies");
        dependencyDiff.AppendLine($"- {request.Package.Value} {currentVersion ?? "(not resolved)"}");
        dependencyDiff.AppendLine($"+ {request.Package.Value} {request.Version.Value}");
        foreach (DependencyEdgeView dependency in source?.Dependencies ?? [])
        {
            dependencyDiff.AppendLine($"+ transitive {dependency.Package.Value} {dependency.VersionRange}");
        }
        return new(
            validation,
            dependencyDiff.ToString(),
            TextDiff(current.Sbom.Json, proposedSbom.Json),
            current.Sbom,
            proposedSbom,
            null,
            null);
    }

    public async ValueTask<SbomExportResult> ExportSbomAsync(
        SbomExportRequest request,
        CancellationToken cancellationToken = default)
    {
        SbomPreviewResult preview = await PreviewSbomAsync(
            new(request.GoalId, request.Scope), cancellationToken);
        if (preview.Sbom is null)
        {
            return new(request.Path, null, 0, preview.ErrorCode, preview.Error);
        }
        SbomExportOutcome result = await sbomExporter.ExportAsync(
            request.Path.Value,
            new(preview.Sbom.Json, preview.Sbom.Sha256),
            request.Overwrite,
            cancellationToken);
        return new(new(result.Path), result.Sha256, result.BytesWritten,
            result.ErrorCode, result.Error);
    }

    private static DependencyInspectionResult Map(DependencyEvidenceSnapshot snapshot) => new(
        snapshot.EntryPoint,
        snapshot.Projects.Select(project => new DependencyProjectView(
            project.ProjectPath,
            project.TargetFrameworks.Select(value => new DependencyTargetFramework(value.Value)).ToArray(),
            project.RuntimeIdentifiers.Select(value => new DependencyRuntime(value.Value)).ToArray(),
            project.Packages.Select(package => new DependencyPackageView(
                new(package.Package.Value),
                package.DeclaredVersion is null ? null : new(package.DeclaredVersion.Value),
                package.CentralVersion is null ? null : new(package.CentralVersion.Value),
                package.ResolvedVersion is null ? null : new(package.ResolvedVersion.Value),
                package.TargetFramework is null ? null : new(package.TargetFramework.Value),
                package.Runtime is null ? null : new(package.Runtime.Value),
                package.IsDirect,
                package.Origins.Select(Map).ToHashSet(),
                package.Dependencies.Select(dependency => new DependencyEdgeView(
                    new(dependency.Package.Value), dependency.VersionRange)).ToArray(),
                package.Sha512,
                package.PackagePath,
                package.Evidence.Select(value => value.Value).ToArray(),
                package.DeclarationCondition,
                package.CentralCondition)).ToArray(),
            project.Conflicts.Select(Map).ToArray(),
            project.HasRestoredAssets,
            project.ErrorCode,
            project.Error)).ToArray(),
        snapshot.Conflicts.Select(Map).ToArray(),
        snapshot.IsTruncated,
        snapshot.ErrorCode,
        snapshot.Error);

    private static DependencyEvidenceOrigin Map(DependencyOrigin value) => value switch
    {
        DependencyOrigin.Declared => DependencyEvidenceOrigin.Declared,
        DependencyOrigin.Central => DependencyEvidenceOrigin.Central,
        DependencyOrigin.Direct => DependencyEvidenceOrigin.Direct,
        DependencyOrigin.Transitive => DependencyEvidenceOrigin.Transitive,
        DependencyOrigin.Locked => DependencyEvidenceOrigin.Locked,
        DependencyOrigin.Restored => DependencyEvidenceOrigin.Restored,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static DependencyConflictView Map(DependencyConflict value) => new(
        new(value.Package.Value), value.Kind, value.Values, value.Message);

    private static PackageSourceEvidenceView Map(PackageCandidateMetadata value) => new(
        value.Source.Value.AbsoluteUri,
        value.Exists,
        value.IsListed,
        value.IsPrerelease,
        value.IsDeprecated,
        value.DeprecationMessage,
        value.LicenseExpression ?? value.LicenseUrl?.AbsoluteUri,
        value.ProjectUrl?.AbsoluteUri,
        value.RepositoryUrl?.AbsoluteUri,
        value.RepositoryCommit,
        value.PublishedSha512,
        value.ComputedSha512,
        value.Dependencies.Select(dependency => new DependencyEdgeView(
            new(dependency.Package.Value), dependency.VersionRange)).ToArray(),
        value.Compatibility.Select(compatibility =>
            $"{compatibility.TargetFramework.Value}: " + (compatibility.IsCompatible switch
            {
                true => "compatible",
                false => "incompatible",
                null => "unknown",
            }) + (compatibility.NearestAssetGroups.Count == 0 ? string.Empty :
                $" ({string.Join(", ", compatibility.NearestAssetGroups)})"))
            .Concat(value.RuntimeCompatibility.Select(compatibility =>
                $"runtime {compatibility.Runtime.Value}: " + (compatibility.IsCompatible switch
                {
                    true => "compatible",
                    false => "incompatible",
                    null => "unknown",
                }) + (compatibility.AvailableRuntimeGroups.Count == 0 ? string.Empty :
                    $" ({string.Join(", ", compatibility.AvailableRuntimeGroups)})"))).ToArray(),
        value.Advisories.Select(advisory =>
            $"severity {advisory.Severity}: {advisory.Url.AbsoluteUri}").ToArray(),
        new(value.RegistrationCitation),
        value.ErrorCode,
        value.Error);

    private static IReadOnlyList<ComponentSpec> ComponentSpecs(DependencyInspectionResult result) =>
        result.Projects.SelectMany(project => project.Packages)
            .Where(package => package.ResolvedVersion is not null)
            .GroupBy(package => (package.Package.Value.ToLowerInvariant(),
                package.ResolvedVersion!.Value.ToLowerInvariant()))
            .Select(group =>
            {
                DependencyPackageView first = group.First();
                return new ComponentSpec(
                    first.Package.Value,
                    first.ResolvedVersion!.Value,
                    group.Select(package => package.Sha512).FirstOrDefault(value => value is not null),
                    License: null,
                    Provenance: group.Select(package => package.PackagePath)
                        .FirstOrDefault(value => value is not null),
                    group.Any(package => package.IsDirect),
                    group.SelectMany(package => package.EvidencePaths)
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                    group.SelectMany(package => package.Dependencies)
                        .Distinct().OrderBy(dependency => dependency.Package.Value,
                            StringComparer.OrdinalIgnoreCase).ToArray());
            })
            .OrderBy(component => component.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(component => component.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static SbomDocument GenerateSbom(IEnumerable<ComponentSpec> input)
    {
        ComponentSpec[] components = input
            .GroupBy(component => (component.Id.ToLowerInvariant(), component.Version.ToLowerInvariant()))
            .Select(group => group.First())
            .OrderBy(component => component.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(component => component.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Dictionary<string, ComponentSpec> byId = components
            .GroupBy(component => component.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("bomFormat", "CycloneDX");
            writer.WriteString("specVersion", "1.6");
            writer.WriteNumber("version", 1);
            writer.WriteStartArray("components");
            foreach (ComponentSpec component in components)
            {
                string reference = Purl(component.Id, component.Version);
                writer.WriteStartObject();
                writer.WriteString("type", "library");
                writer.WriteString("bom-ref", reference);
                writer.WriteString("name", component.Id);
                writer.WriteString("version", component.Version);
                writer.WriteString("purl", reference);
                if (NormalizeSha512(component.Sha512) is { } sha512)
                {
                    writer.WriteStartArray("hashes");
                    writer.WriteStartObject();
                    writer.WriteString("alg", "SHA-512");
                    writer.WriteString("content", sha512);
                    writer.WriteEndObject();
                    writer.WriteEndArray();
                }
                if (component.License is not null)
                {
                    writer.WriteStartArray("licenses");
                    writer.WriteStartObject();
                    writer.WriteString("expression", component.License);
                    writer.WriteEndObject();
                    writer.WriteEndArray();
                }
                writer.WriteStartArray("properties");
                Property(writer, "harness:direct", component.IsDirect ? "true" : "false");
                if (component.Provenance is not null)
                {
                    Property(writer, "harness:provenance", component.Provenance);
                }
                foreach (string evidence in component.Evidence)
                {
                    Property(writer, "harness:evidence", evidence);
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("dependencies");
            foreach (ComponentSpec component in components)
            {
                writer.WriteStartObject();
                writer.WriteString("ref", Purl(component.Id, component.Version));
                writer.WriteStartArray("dependsOn");
                foreach (DependencyEdgeView dependency in component.Dependencies)
                {
                    if (byId.TryGetValue(dependency.Package.Value, out ComponentSpec? target))
                    {
                        writer.WriteStringValue(Purl(target.Id, target.Version));
                    }
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        string json = Encoding.UTF8.GetString(stream.ToArray()) + "\n";
        string sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();
        return new("CycloneDX 1.6 JSON", json, sha256);
    }

    private static void Property(Utf8JsonWriter writer, string name, string value)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);
        writer.WriteString("value", value);
        writer.WriteEndObject();
    }

    private static string Purl(string id, string version) =>
        $"pkg:nuget/{Uri.EscapeDataString(id)}@{Uri.EscapeDataString(version)}";

    private static string? NormalizeSha512(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        string normalized = value.Trim();
        if (normalized.StartsWith("sha512-", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[7..];
        }
        try
        {
            byte[] bytes = Convert.FromBase64String(normalized);
            return bytes.Length == 64 ? Convert.ToHexString(bytes).ToLowerInvariant() : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string TextDiff(string current, string proposed)
    {
        string[] oldLines = current.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string[] newLines = proposed.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        StringBuilder output = new("--- current SBOM\n+++ proposed SBOM\n");
        int maximum = Math.Max(oldLines.Length, newLines.Length);
        for (int index = 0; index < maximum; index++)
        {
            string? before = index < oldLines.Length ? oldLines[index] : null;
            string? after = index < newLines.Length ? newLines[index] : null;
            if (before == after)
            {
                continue;
            }
            if (before is not null)
            {
                output.Append('-').AppendLine(before);
            }
            if (after is not null)
            {
                output.Append('+').AppendLine(after);
            }
        }
        return output.ToString();
    }

    private static DependencyInspectionResult InspectionFailure(string code, string error) =>
        new(string.Empty, [], [], false, code, error);

    private static PackageCandidateValidationResult CandidateFailure(
        DependencyPackageId package,
        DependencyPackageVersion version,
        string code,
        string error) => new(package, version, PackageCandidateDecision.Rejected, [error], [], code, error);

    private sealed record ComponentSpec(
        string Id,
        string Version,
        string? Sha512,
        string? License,
        string? Provenance,
        bool IsDirect,
        IReadOnlyList<string> Evidence,
        IReadOnlyList<DependencyEdgeView> Dependencies);
}
