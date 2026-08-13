using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Harness.DataAccess.Inspection;

namespace Harness.DataAccess.ProjectSecrets;

internal sealed class ProjectUserSecretStore(
    IProjectUserSecretsPathResolver pathResolver) : IProjectUserSecretStore
{
    private const long MaximumProjectBytes = 1024 * 1024;
    private const long MaximumStoreBytes = 4 * 1024 * 1024;
    private const int MaximumSecrets = 4096;
    private const int MaximumKeyLength = 1024;
    private const int MaximumValueLength = 1024 * 1024;
    private const int MaximumWriteAttempts = 3;
    private readonly SemaphoreSlim mutationGate = new(1, 1);

    public async ValueTask<StoredProjectUserSecretsDescriptor> DescribeAsync(
        StoredProjectUserSecretsRequest request,
        CancellationToken cancellationToken = default)
    {
        ProjectResolution resolution = await ResolveProjectAsync(request, cancellationToken);
        if (resolution.Project.State is not StoredProjectUserSecretsState.Available)
        {
            return resolution.Project;
        }

        try
        {
            SecretDocument document = await ReadDocumentAsync(
                resolution.SecretsPath!, cancellationToken);
            return resolution.Project with { SecretCount = document.Values.Count };
        }
        catch (Exception exception) when (IsStoreException(exception))
        {
            return StoreFailure(resolution.Project.ProjectPath);
        }
    }

    public async ValueTask<StoredProjectUserSecretList> ListAsync(
        StoredProjectUserSecretsRequest request,
        CancellationToken cancellationToken = default)
    {
        ProjectResolution resolution = await ResolveProjectAsync(request, cancellationToken);
        if (resolution.Project.State is not StoredProjectUserSecretsState.Available)
        {
            return new(resolution.Project, []);
        }

        try
        {
            SecretDocument document = await ReadDocumentAsync(
                resolution.SecretsPath!, cancellationToken);
            StoredProjectUserSecretKey[] keys = document.Values.Keys
                .Order(StringComparer.OrdinalIgnoreCase)
                .ThenBy(key => key, StringComparer.Ordinal)
                .Select(key => new StoredProjectUserSecretKey(key))
                .ToArray();
            return new(resolution.Project with { SecretCount = keys.Length }, keys);
        }
        catch (Exception exception) when (IsStoreException(exception))
        {
            return new(StoreFailure(resolution.Project.ProjectPath), []);
        }
    }

    public async ValueTask<StoredProjectUserSecretReadResult> ReadAsync(
        StoredProjectUserSecretsRequest request,
        StoredProjectUserSecretKey key,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidKey(key?.Value))
        {
            return ReadFailure("invalid_secret_key", "A valid project secret key is required.");
        }
        string keyValue = key!.Value;

        ProjectResolution resolution = await ResolveProjectAsync(request, cancellationToken);
        if (resolution.Project.State is not StoredProjectUserSecretsState.Available)
        {
            return ReadFailure(
                resolution.Project.ErrorCode ?? "project_user_secrets_unavailable",
                resolution.Project.Error ?? "Project User Secrets are unavailable.");
        }

        try
        {
            SecretDocument document = await ReadDocumentAsync(
                resolution.SecretsPath!, cancellationToken);
            return document.Values.TryGetValue(keyValue, out string? value)
                ? new(StoredProjectUserSecretReadState.Succeeded,
                    new(value), null, null)
                : new(StoredProjectUserSecretReadState.NotFound, null,
                    "secret_not_found", "The selected project secret no longer exists.");
        }
        catch (Exception exception) when (IsStoreException(exception))
        {
            return ReadFailure("project_user_secrets_store_invalid",
                "The project User Secrets store could not be read safely.");
        }
    }

    public ValueTask<StoredProjectUserSecretMutationResult> AddAsync(
        StoredProjectUserSecretsRequest request,
        StoredProjectUserSecretKey key,
        StoredProjectUserSecretValue value,
        CancellationToken cancellationToken = default) =>
        MutateAsync(request, key, value, MutationKind.Add, cancellationToken);

    public ValueTask<StoredProjectUserSecretMutationResult> ChangeAsync(
        StoredProjectUserSecretsRequest request,
        StoredProjectUserSecretKey key,
        StoredProjectUserSecretValue value,
        CancellationToken cancellationToken = default) =>
        MutateAsync(request, key, value, MutationKind.Change, cancellationToken);

    public ValueTask<StoredProjectUserSecretMutationResult> DeleteAsync(
        StoredProjectUserSecretsRequest request,
        StoredProjectUserSecretKey key,
        CancellationToken cancellationToken = default) =>
        MutateAsync(request, key, value: null, MutationKind.Delete, cancellationToken);

    private async ValueTask<StoredProjectUserSecretMutationResult> MutateAsync(
        StoredProjectUserSecretsRequest request,
        StoredProjectUserSecretKey key,
        StoredProjectUserSecretValue? value,
        MutationKind kind,
        CancellationToken cancellationToken)
    {
        if (!IsValidKey(key?.Value))
        {
            return MutationFailure("invalid_secret_key", "A valid project secret key is required.");
        }
        if (kind is not MutationKind.Delete &&
            (value is null || value.Value.Length > MaximumValueLength))
        {
            return MutationFailure("invalid_secret_value",
                $"A project secret value of at most {MaximumValueLength} characters is required.");
        }
        string keyValue = key!.Value;

        ProjectResolution resolution = await ResolveProjectAsync(request, cancellationToken);
        if (resolution.Project.State is not StoredProjectUserSecretsState.Available)
        {
            return new(StoredProjectUserSecretMutationState.Unavailable, resolution.Project,
                resolution.Project.ErrorCode, resolution.Project.Error);
        }

        await mutationGate.WaitAsync(cancellationToken);
        try
        {
            string secretsPath = resolution.SecretsPath!;
            for (int attempt = 0; attempt < MaximumWriteAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SecretDocument document = await ReadDocumentAsync(
                    secretsPath, cancellationToken);
                bool exists = document.Values.ContainsKey(keyValue);
                if (kind is MutationKind.Add && exists)
                {
                    return new(StoredProjectUserSecretMutationState.AlreadyExists,
                        resolution.Project with { SecretCount = document.Values.Count },
                        "secret_already_exists", "A project secret with this key already exists.");
                }
                if (kind is not MutationKind.Add && !exists)
                {
                    return new(StoredProjectUserSecretMutationState.NotFound,
                        resolution.Project with { SecretCount = document.Values.Count },
                        "secret_not_found", "The selected project secret no longer exists.");
                }

                if (kind is MutationKind.Delete)
                {
                    document.Values.Remove(keyValue);
                }
                else
                {
                    document.Values[keyValue] = value!.Value;
                }

                if (await TryWriteAsync(secretsPath, document, cancellationToken))
                {
                    return new(StoredProjectUserSecretMutationState.Succeeded,
                        resolution.Project with { SecretCount = document.Values.Count },
                        null, null);
                }
            }

            return new(StoredProjectUserSecretMutationState.Conflict,
                resolution.Project, "secret_store_changed",
                "The project User Secrets store changed concurrently. Refresh and try again.");
        }
        catch (Exception exception) when (IsStoreException(exception))
        {
            return MutationFailure("project_user_secrets_store_invalid",
                "The project User Secrets store could not be updated safely.");
        }
        finally
        {
            mutationGate.Release();
        }
    }

    private async ValueTask<ProjectResolution> ResolveProjectAsync(
        StoredProjectUserSecretsRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.WorkspaceRoot) ||
            string.IsNullOrWhiteSpace(request.ProjectPath) ||
            !WorkspacePathPolicy.TryResolve(
                request.WorkspaceRoot,
                request.ProjectPath,
                out _,
                out string confinedProject,
                out string projectPath,
                out _,
                out _))
        {
            return ProjectFailure(request?.ProjectPath ?? string.Empty,
                StoredProjectUserSecretsState.ProjectInvalid,
                "project_path_invalid", "The selected project path is invalid.");
        }

        if (Path.GetExtension(projectPath).ToLowerInvariant() is not
            (".csproj" or ".fsproj" or ".vbproj"))
        {
            return ProjectFailure(confinedProject,
                StoredProjectUserSecretsState.ProjectInvalid,
                "project_type_unsupported", "Select an MSBuild project file.");
        }

        FileInfo project = new(projectPath);
        if (!project.Exists)
        {
            return ProjectFailure(confinedProject,
                StoredProjectUserSecretsState.ProjectMissing,
                "project_missing", "The selected project no longer exists.");
        }
        if (project.Length > MaximumProjectBytes || project.LinkTarget is not null)
        {
            return ProjectFailure(confinedProject,
                StoredProjectUserSecretsState.ProjectInvalid,
                "project_metadata_invalid", "The selected project metadata cannot be read safely.");
        }

        try
        {
            XDocument document = await LoadProjectAsync(projectPath, cancellationToken);
            XElement[] identifiers = document.Descendants()
                .Where(element => element.Name.LocalName.Equals(
                    "UserSecretsId", StringComparison.Ordinal))
                .ToArray();
            if (identifiers.Length == 0)
            {
                return ProjectFailure(confinedProject,
                    StoredProjectUserSecretsState.UserSecretsIdMissing,
                    "user_secrets_id_missing",
                    "Add an unconditional literal UserSecretsId to the project before managing secrets.");
            }

            bool conditional = identifiers.Any(element => element.AncestorsAndSelf()
                .Any(owner => owner.Attributes().Any(attribute =>
                    attribute.Name.LocalName.Equals("Condition", StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(attribute.Value))));
            string[] values = identifiers.Select(element => element.Value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (conditional || values.Length != 1 || !IsValidIdentifier(values.SingleOrDefault()))
            {
                return ProjectFailure(confinedProject,
                    StoredProjectUserSecretsState.UserSecretsIdUnsupported,
                    "user_secrets_id_unsupported",
                    "UserSecretsId must be one unconditional literal file-name value in the project.");
            }

            ProjectUserSecretsFilePath secretsPath = pathResolver.Resolve(values[0]);
            ValidateStorePath(secretsPath.Value);
            return new(new(confinedProject, StoredProjectUserSecretsState.Available,
                0, null, null), secretsPath.Value);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          XmlException or InvalidOperationException or ArgumentException)
        {
            return ProjectFailure(confinedProject,
                StoredProjectUserSecretsState.ProjectInvalid,
                "project_metadata_invalid", "The selected project metadata cannot be read safely.");
        }
    }

    private static async ValueTask<XDocument> LoadProjectAsync(
        string path,
        CancellationToken cancellationToken)
    {
        XmlReaderSettings settings = new()
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = MaximumProjectBytes,
            XmlResolver = null,
        };
        await using FileStream stream = File.OpenRead(path);
        using XmlReader reader = XmlReader.Create(stream, settings);
        return await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
    }

    private static async ValueTask<SecretDocument> ReadDocumentAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ValidateStorePath(path);
        FileInfo file = new(path);
        if (!file.Exists)
        {
            return new(new(StringComparer.OrdinalIgnoreCase), ContentHash: null);
        }
        if (file.LinkTarget is not null || file.Length > MaximumStoreBytes)
        {
            throw new InvalidDataException("The project User Secrets store is invalid.");
        }

        byte[] content = await File.ReadAllBytesAsync(path, cancellationToken);
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        using JsonDocument document = JsonDocument.Parse(content,
            new JsonDocumentOptions { MaxDepth = 64, CommentHandling = JsonCommentHandling.Disallow });
        if (document.RootElement.ValueKind is not JsonValueKind.Object)
        {
            throw new InvalidDataException("The project User Secrets root must be an object.");
        }
        Flatten(document.RootElement, prefix: null, values);
        if (values.Count > MaximumSecrets)
        {
            throw new InvalidDataException("The project User Secrets store is too large.");
        }
        return new(values, SHA256.HashData(content));
    }

    private static void Flatten(
        JsonElement element,
        string? prefix,
        Dictionary<string, string> values)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            string key = prefix is null ? property.Name : $"{prefix}:{property.Name}";
            if (!IsValidKey(key))
            {
                throw new InvalidDataException("The project User Secrets store contains an invalid key.");
            }

            if (property.Value.ValueKind is JsonValueKind.Object)
            {
                Flatten(property.Value, key, values);
                continue;
            }
            if (property.Value.ValueKind is not JsonValueKind.String)
            {
                throw new InvalidDataException("Project User Secrets values must be strings.");
            }
            string value = property.Value.GetString()!;
            if (value.Length > MaximumValueLength || !values.TryAdd(key, value))
            {
                throw new InvalidDataException("The project User Secrets store is invalid.");
            }
        }
    }

    private static async ValueTask<bool> TryWriteAsync(
        string path,
        SecretDocument document,
        CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        ValidateStorePath(path);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        byte[]? currentHash = await ReadCurrentHashAsync(path, cancellationToken);
        if (!HashesEqual(currentHash, document.ContentHash))
        {
            return false;
        }

        string temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream,
                    document.Values.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                    new JsonSerializerOptions { WriteIndented = true },
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            byte[]? finalHash = await ReadCurrentHashAsync(path, cancellationToken);
            if (!HashesEqual(finalHash, document.ContentHash))
            {
                return false;
            }
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static async ValueTask<byte[]?> ReadCurrentHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        FileInfo file = new(path);
        if (!file.Exists)
        {
            return null;
        }
        if (file.LinkTarget is not null || file.Length > MaximumStoreBytes)
        {
            throw new InvalidDataException("The project User Secrets store is invalid.");
        }
        return SHA256.HashData(await File.ReadAllBytesAsync(path, cancellationToken));
    }

    private static void ValidateStorePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException("The project User Secrets path is invalid.");
        }
        for (DirectoryInfo? directory = new(Path.GetDirectoryName(path)!);
             directory is not null;
             directory = directory.Parent)
        {
            if (directory.Exists && directory.LinkTarget is not null)
            {
                throw new InvalidOperationException("The project User Secrets path is symbolic.");
            }
        }
    }

    private static bool IsValidIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 256 &&
        value is not "." and not ".." &&
        !value.Contains("$(", StringComparison.Ordinal) &&
        !value.Contains("@(", StringComparison.Ordinal) &&
        !value.Contains("%(", StringComparison.Ordinal) &&
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !value.Contains(Path.DirectorySeparatorChar) &&
        !value.Contains(Path.AltDirectorySeparatorChar);

    private static bool IsValidKey(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumKeyLength &&
        value.Equals(value.Trim(), StringComparison.Ordinal) &&
        !value.Any(char.IsControl) &&
        !value.StartsWith(':') && !value.EndsWith(':') &&
        !value.Contains("::", StringComparison.Ordinal);

    private static bool IsStoreException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or
            InvalidDataException or InvalidOperationException or ArgumentException;

    private static bool HashesEqual(byte[]? left, byte[]? right) =>
        left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

    private static StoredProjectUserSecretReadResult ReadFailure(string code, string error) =>
        new(StoredProjectUserSecretReadState.Unavailable, null, code, error);

    private static StoredProjectUserSecretMutationResult MutationFailure(string code, string error) =>
        new(StoredProjectUserSecretMutationState.Unavailable, null, code, error);

    private static ProjectResolution ProjectFailure(
        string path,
        StoredProjectUserSecretsState state,
        string code,
        string error) => new(new(path, state, 0, code, error), SecretsPath: null);

    private static StoredProjectUserSecretsDescriptor StoreFailure(string path) =>
        new(path, StoredProjectUserSecretsState.StoreInvalid, 0,
            "project_user_secrets_store_invalid",
            "The project User Secrets store could not be read safely.");

    private sealed record ProjectResolution(
        StoredProjectUserSecretsDescriptor Project,
        string? SecretsPath);

    private sealed record SecretDocument(
        Dictionary<string, string> Values,
        byte[]? ContentHash);

    private enum MutationKind
    {
        Add,
        Change,
        Delete,
    }
}
