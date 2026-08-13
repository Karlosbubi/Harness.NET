using System.Text.Json;
using Harness.DataAccess.ProjectSecrets;

namespace Harness.DataAccess.Tests.ProjectSecrets;

public sealed class ProjectUserSecretStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), $"harness-user-secrets-{Guid.NewGuid():N}");
    private readonly string projectPath;
    private readonly string secretsPath;

    public ProjectUserSecretStoreTests()
    {
        Directory.CreateDirectory(root);
        projectPath = Path.Combine(root, "App.csproj");
        secretsPath = Path.Combine(root, "private", "secrets.json");
        File.WriteAllText(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
            "<UserSecretsId>test-project-id</UserSecretsId>" +
            "</PropertyGroup></Project>");
    }

    [Fact]
    public async Task Reads_nested_standard_json_without_disclosing_values()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(secretsPath)!);
        await File.WriteAllTextAsync(secretsPath,
            """
            {
              "Services": {
                "ApiKey": "not-for-output"
              },
              "ConnectionStrings:Main": "also-private"
            }
            """);
        ProjectUserSecretStore store = CreateStore();

        StoredProjectUserSecretList list = await store.ListAsync(Request());
        StoredProjectUserSecretReadResult read = await store.ReadAsync(
            Request(), new("Services:ApiKey"));

        Assert.Equal(StoredProjectUserSecretsState.Available, list.Project.State);
        Assert.Equal(["ConnectionStrings:Main", "Services:ApiKey"],
            list.Keys.Select(key => key.Value));
        Assert.Equal("not-for-output", read.Value?.Value);
        Assert.DoesNotContain("not-for-output", read.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("not-for-output", read.Value!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Add_change_and_delete_are_distinct_and_write_flattened_standard_json()
    {
        ProjectUserSecretStore store = CreateStore();

        StoredProjectUserSecretMutationResult added = await store.AddAsync(
            Request(), new("Services:ApiKey"), new("first"));
        StoredProjectUserSecretMutationResult duplicate = await store.AddAsync(
            Request(), new("Services:ApiKey"), new("other"));
        StoredProjectUserSecretMutationResult changed = await store.ChangeAsync(
            Request(), new("Services:ApiKey"), new("second"));
        StoredProjectUserSecretReadResult read = await store.ReadAsync(
            Request(), new("Services:ApiKey"));

        Assert.Equal(StoredProjectUserSecretMutationState.Succeeded, added.State);
        Assert.Equal(StoredProjectUserSecretMutationState.AlreadyExists, duplicate.State);
        Assert.Equal(StoredProjectUserSecretMutationState.Succeeded, changed.State);
        Assert.Equal("second", read.Value?.Value);
        using (JsonDocument json = JsonDocument.Parse(await File.ReadAllBytesAsync(secretsPath)))
        {
            Assert.Equal("second",
                json.RootElement.GetProperty("Services:ApiKey").GetString());
            Assert.False(json.RootElement.TryGetProperty("Services", out _));
        }
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(secretsPath));
        }

        StoredProjectUserSecretMutationResult deleted = await store.DeleteAsync(
            Request(), new("Services:ApiKey"));
        StoredProjectUserSecretMutationResult missing = await store.DeleteAsync(
            Request(), new("Services:ApiKey"));
        Assert.Equal(StoredProjectUserSecretMutationState.Succeeded, deleted.State);
        Assert.Equal(StoredProjectUserSecretMutationState.NotFound, missing.State);
    }

    [Theory]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk\" />",
        StoredProjectUserSecretsState.UserSecretsIdMissing)]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup Condition=\"'$(X)' == 'Y'\"><UserSecretsId>id</UserSecretsId></PropertyGroup></Project>",
        StoredProjectUserSecretsState.UserSecretsIdUnsupported)]
    [InlineData("<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><UserSecretsId>$(Computed)</UserSecretsId></PropertyGroup></Project>",
        StoredProjectUserSecretsState.UserSecretsIdUnsupported)]
    public async Task Rejects_missing_conditional_or_computed_project_identifiers(
        string project,
        StoredProjectUserSecretsState expected)
    {
        await File.WriteAllTextAsync(projectPath, project);

        StoredProjectUserSecretsDescriptor result = await CreateStore().DescribeAsync(Request());

        Assert.Equal(expected, result.State);
        Assert.False(File.Exists(secretsPath));
    }

    [Fact]
    public async Task Invalid_store_shape_fails_closed_and_is_not_rewritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(secretsPath)!);
        const string invalid = "{ \"Secret\": 42 }";
        await File.WriteAllTextAsync(secretsPath, invalid);
        ProjectUserSecretStore store = CreateStore();

        StoredProjectUserSecretsDescriptor described = await store.DescribeAsync(Request());
        StoredProjectUserSecretMutationResult changed = await store.ChangeAsync(
            Request(), new("Secret"), new("replacement"));

        Assert.Equal(StoredProjectUserSecretsState.StoreInvalid, described.State);
        Assert.Equal(StoredProjectUserSecretMutationState.Unavailable, changed.State);
        Assert.Equal(invalid, await File.ReadAllTextAsync(secretsPath));
    }

    [Fact]
    public void Platform_resolver_uses_the_standard_current_user_location()
    {
        string path = new PlatformProjectUserSecretsPathResolver()
            .Resolve("representative-id").Value;

        string expected = OperatingSystem.IsWindows()
            ? Path.Combine("Microsoft", "UserSecrets", "representative-id", "secrets.json")
            : Path.Combine(".microsoft", "usersecrets", "representative-id", "secrets.json");
        Assert.True(Path.IsPathFullyQualified(path));
        Assert.EndsWith(expected, path, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private ProjectUserSecretStore CreateStore() => new(new FixedPath(secretsPath));

    private StoredProjectUserSecretsRequest Request() => new(root, "App.csproj");

    private sealed class FixedPath(string path) : IProjectUserSecretsPathResolver
    {
        public ProjectUserSecretsFilePath Resolve(string userSecretsId) => new(path);
    }
}
