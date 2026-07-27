using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Construction;
using Microsoft.Build.Exceptions;

namespace Harness.DataAccess.Inspection;

internal sealed class WorkspaceDotNetInspector : IWorkspaceDotNetInspector
{
    private const int MaximumProjects = 200;
    private const int MaximumReferencesPerProject = 500;
    private const long MaximumMetadataBytes = 1024 * 1024;

    public async ValueTask<WorkspaceDotNetInfo> InspectAsync(
        string workspaceRoot,
        string entryPoint,
        CancellationToken cancellationToken = default)
    {
        string relativeEntryPoint;
        try
        {
            relativeEntryPoint = Path.IsPathRooted(entryPoint)
                ? Path.GetRelativePath(Path.GetFullPath(workspaceRoot), Path.GetFullPath(entryPoint))
                : entryPoint;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return Failure(entryPoint, "invalid_entry_point", exception.Message);
        }

        if (!WorkspacePathPolicy.TryResolve(
                workspaceRoot,
                relativeEntryPoint,
                out string root,
                out string confinedEntryPoint,
                out string entryPointPath,
                out string? errorCode,
                out string? error))
        {
            return Failure(confinedEntryPoint, errorCode!, error!);
        }

        FileInfo entryPointFile = new(entryPointPath);
        if (!entryPointFile.Exists || entryPointFile.Length > MaximumMetadataBytes)
        {
            return Failure(
                confinedEntryPoint,
                entryPointFile.Exists ? "entry_point_too_large" : "entry_point_missing",
                entryPointFile.Exists
                    ? "The entry point exceeds the 1 MiB metadata limit."
                    : "The selected entry point does not exist.");
        }

        try
        {
            string extension = entryPointFile.Extension.ToLowerInvariant();
            IReadOnlyList<string> projectPaths = extension switch
            {
                ".sln" => ReadSolutionProjects(entryPointFile.FullName),
                ".slnx" => await ReadSolutionXmlProjectsAsync(entryPointFile.FullName, cancellationToken),
                ".csproj" or ".fsproj" or ".vbproj" => [entryPointFile.FullName],
                _ => [],
            };
            if (projectPaths.Count == 0 && extension is not ".csproj" and not ".fsproj" and not ".vbproj")
            {
                return Failure(
                    confinedEntryPoint,
                    "unsupported_entry_point",
                    "The entry point must be a .sln, .slnx, or MSBuild project file.");
            }

            bool isTruncated = projectPaths.Count > MaximumProjects;
            List<DotNetProjectInfo> projects = [];
            foreach (string projectPath in projectPaths.Take(MaximumProjects))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fullCandidate = Path.IsPathRooted(projectPath)
                    ? Path.GetFullPath(projectPath)
                    : Path.GetFullPath(projectPath, entryPointFile.DirectoryName!);
                string relativeProject = Path.GetRelativePath(root, fullCandidate);
                if (!WorkspacePathPolicy.TryResolve(
                        root,
                        relativeProject,
                        out _,
                        out string confinedProject,
                        out string fullProjectPath,
                        out _,
                        out _))
                {
                    continue;
                }

                DotNetProjectInfo? project = await ReadProjectAsync(
                    fullProjectPath,
                    confinedProject,
                    cancellationToken);
                if (project is not null)
                {
                    projects.Add(project);
                    isTruncated |= project.References.Count >= MaximumReferencesPerProject;
                }
            }

            DotNetSdkPolicy? sdkPolicy = await ReadSdkPolicyAsync(root, cancellationToken);
            return new(
                confinedEntryPoint,
                extension.TrimStart('.'),
                sdkPolicy,
                projects,
                isTruncated,
                ErrorCode: null,
                Error: null);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or XmlException or
            JsonException or InvalidProjectFileException)
        {
            return Failure(confinedEntryPoint, "metadata_invalid", exception.Message);
        }
    }

    private static IReadOnlyList<string> ReadSolutionProjects(string solutionPath) =>
        SolutionFile.Parse(solutionPath).ProjectsInOrder
            .Where(project => IsProjectFile(project.RelativePath))
            .Select(project => project.AbsolutePath)
            .ToArray();

    private static async ValueTask<IReadOnlyList<string>> ReadSolutionXmlProjectsAsync(
        string solutionPath,
        CancellationToken cancellationToken)
    {
        XDocument document = await LoadXmlAsync(solutionPath, cancellationToken);
        string directory = Path.GetDirectoryName(solutionPath)!;
        return document.Descendants()
            .Where(element => element.Name.LocalName.Equals("Project", StringComparison.Ordinal))
            .Select(element => element.Attribute("Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path) && IsProjectFile(path))
            .Select(path => Path.GetFullPath(path!, directory))
            .ToArray();
    }

    private static async ValueTask<DotNetProjectInfo?> ReadProjectAsync(
        string projectPath,
        string relativePath,
        CancellationToken cancellationToken)
    {
        FileInfo file = new(projectPath);
        if (!file.Exists || file.Length > MaximumMetadataBytes)
        {
            return null;
        }

        XDocument document = await LoadXmlAsync(projectPath, cancellationToken);
        XElement? root = document.Root;
        if (root is null)
        {
            return null;
        }

        string? sdk = root.Attribute("Sdk")?.Value ?? root.Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals("Sdk", StringComparison.Ordinal))
            ?.Attribute("Name")?.Value;
        string[] targetFrameworks = Values(document, "TargetFramework", "TargetFrameworks")
            .SelectMany(value => value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        DotNetReferenceInfo[] references = document.Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference")
            .Select(element => new DotNetReferenceInfo(
                element.Name.LocalName == "PackageReference" ? "package" : "project",
                element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value ?? string.Empty,
                element.Attribute("Version")?.Value ?? ChildValue(element, "Version")))
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Identity))
            .Take(MaximumReferencesPerProject)
            .ToArray();
        return new(
            relativePath,
            sdk,
            targetFrameworks,
            Values(document, "LangVersion").LastOrDefault(),
            Values(document, "Nullable").LastOrDefault(),
            references);
    }

    private static async ValueTask<DotNetSdkPolicy?> ReadSdkPolicyAsync(
        string root,
        CancellationToken cancellationToken)
    {
        if (!WorkspacePathPolicy.TryResolve(
                root,
                "global.json",
                out _,
                out _,
                out string path,
                out _,
                out _))
        {
            return null;
        }

        FileInfo file = new(path);
        if (!file.Exists || file.Length > MaximumMetadataBytes)
        {
            return null;
        }

        await using FileStream stream = file.OpenRead();
        using JsonDocument document = await JsonDocument.ParseAsync(
            stream,
            new JsonDocumentOptions { MaxDepth = 16 },
            cancellationToken);
        if (!document.RootElement.TryGetProperty("sdk", out JsonElement sdk))
        {
            return null;
        }

        return new(
            StringValue(sdk, "version"),
            StringValue(sdk, "rollForward"),
            BooleanValue(sdk, "allowPrerelease"));
    }

    private static async ValueTask<XDocument> LoadXmlAsync(
        string path,
        CancellationToken cancellationToken)
    {
        XmlReaderSettings settings = new()
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = MaximumMetadataBytes,
            XmlResolver = null,
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
        element.Elements()
            .FirstOrDefault(child => child.Name.LocalName.Equals(name, StringComparison.Ordinal))
            ?.Value.Trim();

    private static string? StringValue(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? BooleanValue(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static bool IsProjectFile(string path) =>
        path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase);

    private static WorkspaceDotNetInfo Failure(string entryPoint, string code, string error) =>
        new(entryPoint, string.Empty, null, [], IsTruncated: false, code, error);
}
