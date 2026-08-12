using System.Runtime.Versioning;
using Harness.DataAccess.Secrets;

namespace Harness.Host.Tests;

[SupportedOSPlatform("linux")]
public sealed class EvaluationMcpTokenBootstrapTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "harness-evaluation-token-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Seeds_valid_owner_only_token_and_removes_bootstrap_file()
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "mcp.token");
        string token = Convert.ToBase64String(Enumerable.Range(0, 48).Select(value => (byte)value).ToArray());
        await File.WriteAllTextAsync(path, token);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        RecordingSecretStore secrets = new();

        await EvaluationMcpTokenBootstrap.SeedAsync(root, path, secrets);

        Assert.False(File.Exists(path));
        Assert.Equal(EvaluationMcpTokenBootstrap.TokenReference, secrets.Reference?.Name);
        Assert.Equal(token, secrets.Value);
    }

    [Fact]
    public async Task Rejects_and_removes_invalid_token()
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "mcp.token");
        await File.WriteAllTextAsync(path, "not-a-token");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await EvaluationMcpTokenBootstrap.SeedAsync(root, path, new RecordingSecretStore()));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Rejects_token_outside_evaluation_root()
    {
        Directory.CreateDirectory(root);
        string other = Path.Combine(Path.GetTempPath(), $"harness-token-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(other, Convert.ToBase64String(new byte[48]));
        File.SetUnixFileMode(other, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await EvaluationMcpTokenBootstrap.SeedAsync(root, other, new RecordingSecretStore()));
        }
        finally
        {
            File.Delete(other);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class RecordingSecretStore : ISecretStore
    {
        public SecretReference? Reference { get; private set; }
        public string? Value { get; private set; }

        public ValueTask<string?> GetAsync(
            SecretReference reference, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Value);

        public ValueTask SetAsync(
            SecretReference reference, string value,
            CancellationToken cancellationToken = default)
        {
            Reference = reference;
            Value = value;
            return ValueTask.CompletedTask;
        }
    }
}
