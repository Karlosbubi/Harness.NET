using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Harness.DataAccess.Research;

internal sealed partial class DependencyEvidenceReader : IDependencyEvidenceReader
{
    private const int MaximumProjects = 200;
    private const int MaximumPackagesPerProject = 5_000;
    private const long MaximumXmlBytes = 4 * 1024 * 1024;
    private const long MaximumAssetsBytes = 128 * 1024 * 1024;
    private const long MaximumLockBytes = 16 * 1024 * 1024;

    public async ValueTask<DependencyEvidenceSnapshot> InspectAsync(
        string workspaceRoot,
        string entryPoint,
        CancellationToken cancellationToken = default)
    {
        string root;
        string selected;
        try
        {
            root = Path.GetFullPath(workspaceRoot);
            selected = Path.GetFullPath(entryPoint, root);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return Failure(entryPoint, "invalid_entry_point", exception.Message);
        }

        if (!Inside(root, selected) || !File.Exists(selected))
        {
            return Failure(entryPoint, "entry_point_unavailable",
                "The entry point must be an existing solution or project inside the workspace.");
        }

        try
        {
            IReadOnlyList<string> projectPaths = await DiscoverProjectsAsync(
                root, selected, cancellationToken);
            bool truncated = projectPaths.Count > MaximumProjects;
            List<DependencyProjectEvidence> projects = [];
            foreach (string projectPath in projectPaths.Take(MaximumProjects))
            {
                cancellationToken.ThrowIfCancellationRequested();
                projects.Add(await ReadProjectAsync(root, projectPath, cancellationToken));
                truncated |= projects[^1].Packages.Count >= MaximumPackagesPerProject;
            }

            DependencyConflict[] conflicts = projects.SelectMany(project => project.Conflicts)
                .GroupBy(conflict => (conflict.Package.Value, conflict.Kind),
                    StringTupleComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(conflict => conflict.Package.Value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(conflict => conflict.Kind, StringComparer.Ordinal)
                .ToArray();
            return new(
                Path.GetRelativePath(root, selected).Replace('\\', '/'),
                projects,
                conflicts,
                truncated,
                null,
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            XmlException or JsonException or InvalidDataException)
        {
            return Failure(Path.GetRelativePath(root, selected).Replace('\\', '/'),
                "dependency_metadata_invalid", exception.Message);
        }
    }

    private static async ValueTask<IReadOnlyList<string>> DiscoverProjectsAsync(
        string root,
        string entryPoint,
        CancellationToken cancellationToken)
    {
        string extension = Path.GetExtension(entryPoint).ToLowerInvariant();
        IEnumerable<string> candidates = extension switch
        {
            ".csproj" or ".fsproj" or ".vbproj" => [entryPoint],
            ".slnx" => await ReadSolutionXmlAsync(entryPoint, cancellationToken),
            ".sln" => ReadSolutionAsync(entryPoint),
            _ => [],
        };
        return candidates
            .Select(Path.GetFullPath)
            .Where(path => Inside(root, path) && IsProject(path) && File.Exists(path))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(MaximumProjects + 1)
            .ToArray();
    }

    private static IEnumerable<string> ReadSolutionAsync(string path)
    {
        string directory = Path.GetDirectoryName(path)!;
        return File.ReadLines(path)
            .Select(line => SolutionProjectRegex().Match(line))
            .Where(match => match.Success)
            .Select(match => Path.GetFullPath(
                match.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar), directory));
    }

    private static async ValueTask<IEnumerable<string>> ReadSolutionXmlAsync(
        string path,
        CancellationToken cancellationToken)
    {
        XDocument document = await LoadXmlAsync(path, cancellationToken);
        string directory = Path.GetDirectoryName(path)!;
        return document.Descendants()
            .Where(element => element.Name.LocalName == "Project")
            .Select(element => element.Attribute("Path")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFullPath(value!, directory))
            .ToArray();
    }

    private static async ValueTask<DependencyProjectEvidence> ReadProjectAsync(
        string root,
        string projectPath,
        CancellationToken cancellationToken)
    {
        string relativeProject = Relative(root, projectPath);
        FileInfo file = new(projectPath);
        if (file.Length > MaximumXmlBytes)
        {
            return new(relativeProject, [], [], [], [], false, "project_too_large",
                "The project file exceeds the 4 MiB metadata limit.");
        }

        XDocument project = await LoadXmlAsync(projectPath, cancellationToken);
        Dictionary<string, CentralVersion> central = await ReadCentralVersionsAsync(
            root, Path.GetDirectoryName(projectPath)!, cancellationToken);
        string[] frameworks = Values(project, "TargetFramework", "TargetFrameworks")
            .SelectMany(value => value.Split(';', StringSplitOptions.TrimEntries |
                StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] runtimes = Values(project, "RuntimeIdentifier", "RuntimeIdentifiers")
            .SelectMany(value => value.Split(';', StringSplitOptions.TrimEntries |
                StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        List<DeclaredPackage> declared = project.Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element =>
            {
                string id = element.Attribute("Include")?.Value ??
                    element.Attribute("Update")?.Value ?? string.Empty;
                string? version = element.Attribute("VersionOverride")?.Value ??
                    ChildValue(element, "VersionOverride") ??
                    element.Attribute("Version")?.Value ??
                    ChildValue(element, "Version");
                central.TryGetValue(id, out CentralVersion? centralVersion);
                return new DeclaredPackage(
                    id,
                    version,
                    centralVersion?.Version,
                    EffectiveCondition(element),
                    relativeProject,
                    centralVersion?.Path,
                    centralVersion?.Condition);
            })
            .Where(package => package.Id.Length > 0)
            .ToList();

        string assetsPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "obj", "project.assets.json");
        List<PackageDependencyEvidence> packages = [];
        List<DependencyConflict> conflicts = DeclaredConflicts(declared);
        if (File.Exists(assetsPath))
        {
            FileInfo assets = new(assetsPath);
            if (assets.Length > MaximumAssetsBytes)
            {
                return new(relativeProject, frameworks.Select(value => new TargetFrameworkMoniker(value)).ToArray(),
                    runtimes.Select(value => new RuntimeIdentifier(value)).ToArray(), [], conflicts, true,
                    "assets_too_large", "project.assets.json exceeds the 128 MiB metadata limit.");
            }
            await using FileStream stream = File.OpenRead(assetsPath);
            using JsonDocument document = await JsonDocument.ParseAsync(stream,
                new JsonDocumentOptions { MaxDepth = 128 }, cancellationToken);
            ReadRestoredPackages(
                root, relativeProject, Relative(root, assetsPath), document.RootElement,
                declared, packages, conflicts);
        }

        string lockPath = Path.Combine(Path.GetDirectoryName(projectPath)!, "packages.lock.json");
        if (File.Exists(lockPath))
        {
            await ReadLockedPackagesAsync(root, relativeProject, lockPath, declared, packages,
                conflicts, cancellationToken);
        }

        AddUnresolvedDeclarations(declared, packages);
        PackageDependencyEvidence[] ordered = packages
            .Take(MaximumPackagesPerProject)
            .OrderBy(package => package.Package.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.ResolvedVersion?.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.TargetFramework?.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.Runtime?.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        conflicts.AddRange(ResolvedConflicts(ordered));
        return new(
            relativeProject,
            frameworks.Select(value => new TargetFrameworkMoniker(value)).ToArray(),
            runtimes.Select(value => new RuntimeIdentifier(value)).ToArray(),
            ordered,
            conflicts.Distinct().ToArray(),
            File.Exists(assetsPath),
            null,
            null);
    }

    private static async ValueTask ReadLockedPackagesAsync(
        string root,
        string projectPath,
        string lockPath,
        IReadOnlyList<DeclaredPackage> declared,
        List<PackageDependencyEvidence> packages,
        ICollection<DependencyConflict> conflicts,
        CancellationToken cancellationToken)
    {
        FileInfo file = new(lockPath);
        if (file.Length > MaximumLockBytes)
        {
            conflicts.Add(new(new("(lock file)"), "lock_file_too_large", [],
                "packages.lock.json exceeds the 16 MiB metadata limit."));
            return;
        }
        await using FileStream stream = File.OpenRead(lockPath);
        using JsonDocument document = await JsonDocument.ParseAsync(stream,
            new JsonDocumentOptions { MaxDepth = 96 }, cancellationToken);
        if (!document.RootElement.TryGetProperty("dependencies", out JsonElement frameworks) ||
            frameworks.ValueKind != JsonValueKind.Object)
        {
            conflicts.Add(new(new("(lock file)"), "lock_dependencies_missing", [],
                "packages.lock.json has no dependencies object."));
            return;
        }
        string relativeLock = Relative(root, lockPath);
        foreach (JsonProperty framework in frameworks.EnumerateObject())
        {
            if (framework.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            foreach (JsonProperty item in framework.Value.EnumerateObject())
            {
                if (item.Value.ValueKind != JsonValueKind.Object ||
                    StringValue(item.Value, "resolved") is not { Length: > 0 } resolved)
                {
                    continue;
                }
                bool isDirect = StringValue(item.Value, "type")?.Equals("Direct",
                    StringComparison.OrdinalIgnoreCase) == true;
                DeclaredPackage? direct = declared.FirstOrDefault(package =>
                    package.Id.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
                int existingIndex = packages.FindIndex(package =>
                    package.Package.Value.Equals(item.Name, StringComparison.OrdinalIgnoreCase) &&
                    package.ResolvedVersion?.Value.Equals(resolved, StringComparison.OrdinalIgnoreCase) == true &&
                    package.TargetFramework?.Value.Equals(framework.Name,
                        StringComparison.OrdinalIgnoreCase) == true);
                HashSet<DependencyOrigin> origins = [DependencyOrigin.Locked];
                origins.Add(isDirect ? DependencyOrigin.Direct : DependencyOrigin.Transitive);
                if (direct is not null)
                {
                    origins.Add(DependencyOrigin.Declared);
                    if (direct.CentralVersion is not null)
                    {
                        origins.Add(DependencyOrigin.Central);
                    }
                }
                IReadOnlyList<PackageDependencyEdge> dependencies = ReadDependencies(item.Value);
                if (existingIndex >= 0)
                {
                    PackageDependencyEvidence existing = packages[existingIndex];
                    packages[existingIndex] = existing with
                    {
                        Origins = existing.Origins.Concat(origins).ToHashSet(),
                        Dependencies = existing.Dependencies.Concat(dependencies).Distinct()
                            .OrderBy(dependency => dependency.Package.Value,
                                StringComparer.OrdinalIgnoreCase).ToArray(),
                        Sha512 = existing.Sha512 ?? StringValue(item.Value, "contentHash"),
                        Evidence = existing.Evidence.Append(new(relativeLock)).Distinct().ToArray(),
                    };
                    continue;
                }
                string[] conflicting = packages.Where(package =>
                        package.Package.Value.Equals(item.Name, StringComparison.OrdinalIgnoreCase) &&
                        package.TargetFramework?.Value.Equals(framework.Name,
                            StringComparison.OrdinalIgnoreCase) == true &&
                        package.ResolvedVersion is not null)
                    .Select(package => package.ResolvedVersion!.Value)
                    .Append(resolved)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (conflicting.Length > 1)
                {
                    conflicts.Add(new(new(item.Name), "lock_restored_version_conflict", conflicting,
                        "packages.lock.json and restored assets resolve different versions."));
                }
                packages.Add(new(
                    new(item.Name),
                    direct?.DeclaredVersion is null ? null : new(direct.DeclaredVersion),
                    direct?.CentralVersion is null ? null : new(direct.CentralVersion),
                    new(resolved),
                    new(framework.Name),
                    null,
                    isDirect || direct is not null,
                    origins,
                    dependencies,
                    StringValue(item.Value, "contentHash"),
                    null,
                    EvidencePaths(projectPath, relativeLock, direct),
                    direct?.Condition,
                    direct?.CentralCondition));
            }
        }
    }

    private static void ReadRestoredPackages(
        string root,
        string projectPath,
        string assetsPath,
        JsonElement document,
        IReadOnlyList<DeclaredPackage> declared,
        ICollection<PackageDependencyEvidence> output,
        ICollection<DependencyConflict> conflicts)
    {
        if (!document.TryGetProperty("targets", out JsonElement targets) ||
            targets.ValueKind != JsonValueKind.Object)
        {
            conflicts.Add(new(new("(assets)"), "missing_targets", [],
                $"{assetsPath} has no NuGet targets object."));
            return;
        }

        Dictionary<string, LibraryMetadata> libraries = ReadLibraries(document);
        foreach (JsonProperty target in targets.EnumerateObject())
        {
            (string framework, string? runtime) = SplitTarget(target.Name);
            if (target.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            foreach (JsonProperty item in target.Value.EnumerateObject())
            {
                if (!SplitIdentity(item.Name, out string id, out string version) ||
                    item.Value.ValueKind != JsonValueKind.Object ||
                    (item.Value.TryGetProperty("type", out JsonElement type) &&
                     type.ValueKind == JsonValueKind.String && type.GetString() != "package"))
                {
                    continue;
                }

                DeclaredPackage? direct = declared.FirstOrDefault(package =>
                    package.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                libraries.TryGetValue(item.Name, out LibraryMetadata? library);
                HashSet<DependencyOrigin> origins = [DependencyOrigin.Restored];
                if (direct is null)
                {
                    origins.Add(DependencyOrigin.Transitive);
                }
                else
                {
                    origins.Add(DependencyOrigin.Declared);
                    origins.Add(DependencyOrigin.Direct);
                    if (direct.CentralVersion is not null)
                    {
                        origins.Add(DependencyOrigin.Central);
                    }
                }
                output.Add(new(
                    new(id),
                    direct?.DeclaredVersion is null ? null : new(direct.DeclaredVersion),
                    direct?.CentralVersion is null ? null : new(direct.CentralVersion),
                    new(version),
                    new(framework),
                    runtime is null ? null : new(runtime),
                    direct is not null,
                    origins,
                    ReadDependencies(item.Value),
                    library?.Sha512,
                    library?.Path,
                    EvidencePaths(projectPath, assetsPath, direct),
                    direct?.Condition,
                    direct?.CentralCondition));
            }
        }
    }

    private static Dictionary<string, LibraryMetadata> ReadLibraries(JsonElement document)
    {
        Dictionary<string, LibraryMetadata> result = new(StringComparer.OrdinalIgnoreCase);
        if (!document.TryGetProperty("libraries", out JsonElement libraries) ||
            libraries.ValueKind != JsonValueKind.Object)
        {
            return result;
        }
        foreach (JsonProperty library in libraries.EnumerateObject())
        {
            string? type = StringValue(library.Value, "type");
            if (type is not null && type != "package")
            {
                continue;
            }
            result[library.Name] = new(StringValue(library.Value, "sha512"),
                StringValue(library.Value, "path"));
        }
        return result;
    }

    private static IReadOnlyList<PackageDependencyEdge> ReadDependencies(JsonElement package)
    {
        if (!package.TryGetProperty("dependencies", out JsonElement dependencies) ||
            dependencies.ValueKind != JsonValueKind.Object)
        {
            return [];
        }
        return dependencies.EnumerateObject()
            .Select(dependency => new PackageDependencyEdge(
                new(dependency.Name),
                dependency.Value.ValueKind == JsonValueKind.String
                    ? dependency.Value.GetString() ?? string.Empty
                    : dependency.Value.GetRawText()))
            .OrderBy(dependency => dependency.Package.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddUnresolvedDeclarations(
        IReadOnlyList<DeclaredPackage> declared,
        ICollection<PackageDependencyEvidence> packages)
    {
        foreach (DeclaredPackage item in declared.Where(item => !packages.Any(package =>
                     package.Package.Value.Equals(item.Id, StringComparison.OrdinalIgnoreCase)))
                 .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            HashSet<DependencyOrigin> origins = [DependencyOrigin.Declared, DependencyOrigin.Direct];
            if (item.CentralVersion is not null)
            {
                origins.Add(DependencyOrigin.Central);
            }
            packages.Add(new(
                new(item.Id),
                item.DeclaredVersion is null ? null : new(item.DeclaredVersion),
                item.CentralVersion is null ? null : new(item.CentralVersion),
                null,
                null,
                null,
                true,
                origins,
                [],
                null,
                null,
                EvidencePaths(item.ProjectPath, null, item),
                item.Condition,
                item.CentralCondition));
        }
    }

    private static IReadOnlyList<DependencyEvidencePath> EvidencePaths(
        string projectPath,
        string? assetsPath,
        DeclaredPackage? direct)
    {
        List<DependencyEvidencePath> paths = [];
        if (direct is not null)
        {
            paths.Add(new(projectPath));
            if (direct.CentralPath is not null)
            {
                paths.Add(new(direct.CentralPath));
            }
        }
        if (assetsPath is not null)
        {
            paths.Add(new(assetsPath));
        }
        return paths.Distinct().ToArray();
    }

    private static List<DependencyConflict> DeclaredConflicts(IReadOnlyList<DeclaredPackage> packages) =>
        packages.GroupBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                string[] versions = group.Select(package => package.DeclaredVersion ?? package.CentralVersion ??
                        "(unresolved)")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return versions.Length > 1
                    ? new[] { new DependencyConflict(new(group.Key), "declared_version_conflict", versions,
                        "The project declares more than one version for this package.") }
                    : [];
            }).ToList();

    private static IEnumerable<DependencyConflict> ResolvedConflicts(
        IReadOnlyList<PackageDependencyEvidence> packages) =>
        packages.Where(package => package.ResolvedVersion is not null)
            .GroupBy(package => package.Package.Value, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Id = group.Key,
                Versions = group.Select(package => package.ResolvedVersion!.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            })
            .Where(group => group.Versions.Length > 1)
            .Select(group => new DependencyConflict(new(group.Id), "resolved_version_conflict",
                group.Versions, "Different target graphs resolved different package versions."));

    private static async ValueTask<Dictionary<string, CentralVersion>> ReadCentralVersionsAsync(
        string root,
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        string? current = projectDirectory;
        while (current is not null && Inside(root, current))
        {
            string candidate = Path.Combine(current, "Directory.Packages.props");
            if (File.Exists(candidate))
            {
                XDocument document = await LoadXmlAsync(candidate, cancellationToken);
                string relative = Relative(root, candidate);
                return document.Descendants()
                    .Where(element => element.Name.LocalName == "PackageVersion")
                    .Select(element => new
                    {
                        Id = element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value,
                        Version = element.Attribute("Version")?.Value ?? ChildValue(element, "Version"),
                        Condition = EffectiveCondition(element),
                    })
                    .Where(item => !string.IsNullOrWhiteSpace(item.Id) &&
                        !string.IsNullOrWhiteSpace(item.Version))
                    .GroupBy(item => item.Id!, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key,
                        group => new CentralVersion(group.Last().Version!, relative,
                            group.Last().Condition),
                        StringComparer.OrdinalIgnoreCase);
            }
            if (Path.GetFullPath(current).Equals(root, StringComparison.Ordinal))
            {
                break;
            }
            current = Directory.GetParent(current)?.FullName;
        }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    private static async ValueTask<XDocument> LoadXmlAsync(
        string path,
        CancellationToken cancellationToken)
    {
        FileInfo file = new(path);
        if (file.Length > MaximumXmlBytes)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} exceeds the XML metadata limit.");
        }
        XmlReaderSettings settings = new()
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumXmlBytes,
        };
        await using FileStream stream = File.OpenRead(path);
        using XmlReader reader = XmlReader.Create(stream, settings);
        return await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
    }

    private static IEnumerable<string> Values(XContainer document, params string[] names) =>
        document.Descendants()
            .Where(element => names.Contains(element.Name.LocalName, StringComparer.Ordinal))
            .Select(element => element.Value.Trim())
            .Where(value => value.Length > 0);

    private static string? ChildValue(XContainer element, string name) =>
        element.Elements().FirstOrDefault(child => child.Name.LocalName == name)?.Value.Trim();

    private static string? EffectiveCondition(XElement element)
    {
        string[] conditions = element.AncestorsAndSelf()
            .Reverse()
            .Select(item => item.Attribute("Condition")?.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
        return conditions.Length switch
        {
            0 => null,
            1 => conditions[0],
            _ => string.Join(" && ", conditions.Select(condition => $"({condition})")),
        };
    }

    private static string? StringValue(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static (string Framework, string? Runtime) SplitTarget(string value)
    {
        int separator = value.IndexOf('/');
        return separator < 0 ? (value, null) : (value[..separator], value[(separator + 1)..]);
    }

    private static bool SplitIdentity(string value, out string id, out string version)
    {
        int separator = value.LastIndexOf('/');
        id = separator > 0 ? value[..separator] : string.Empty;
        version = separator > 0 && separator < value.Length - 1 ? value[(separator + 1)..] : string.Empty;
        return id.Length > 0 && version.Length > 0;
    }

    private static bool IsProject(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".csproj" or ".fsproj" or ".vbproj";

    private static bool Inside(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar,
            StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static DependencyEvidenceSnapshot Failure(string entryPoint, string code, string error) =>
        new(entryPoint, [], [], false, code, error);

    [GeneratedRegex("^Project\\(\"[^\"]+\"\\)\\s*=\\s*\"[^\"]*\"\\s*,\\s*\"(?<path>[^\"]+)\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex SolutionProjectRegex();

    private sealed record DeclaredPackage(
        string Id,
        string? DeclaredVersion,
        string? CentralVersion,
        string? Condition,
        string ProjectPath,
        string? CentralPath,
        string? CentralCondition);

    private sealed record CentralVersion(string Version, string Path, string? Condition);

    private sealed record LibraryMetadata(string? Sha512, string? Path);

    private sealed class StringTupleComparer : IEqualityComparer<(string First, string Second)>
    {
        internal static StringTupleComparer OrdinalIgnoreCase { get; } = new();

        public bool Equals((string First, string Second) x, (string First, string Second) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.First, y.First) &&
            StringComparer.Ordinal.Equals(x.Second, y.Second);

        public int GetHashCode((string First, string Second) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.First),
                StringComparer.Ordinal.GetHashCode(obj.Second));
    }
}
