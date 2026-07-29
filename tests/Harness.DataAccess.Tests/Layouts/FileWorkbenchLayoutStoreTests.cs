using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Layouts;

namespace Harness.DataAccess.Tests.Layouts;

public sealed class FileWorkbenchLayoutStoreTests : IDisposable
{
    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        "harness-layout-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Missing_round_trip_overwrite_and_reset_are_deterministic()
    {
        StubApplicationPaths paths = new(CreatePaths());
        FileWorkbenchLayoutStore store = new(paths);

        Assert.Null((await store.ReadAsync()).Layout);
        Assert.True((await store.WriteAsync(new("{\"dock\":1}"))).Succeeded);
        Assert.Equal("{\"dock\":1}", (await store.ReadAsync()).Layout?.Value);
        Assert.True((await store.WriteAsync(new("{\"dock\":2}"))).Succeeded);
        Assert.Equal("{\"dock\":2}", (await store.ReadAsync()).Layout?.Value);

        if (OperatingSystem.IsLinux())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(paths.Current.WorkbenchLayoutPath));
        }

        Assert.True((await store.ResetAsync()).Succeeded);
        Assert.False(File.Exists(paths.Current.WorkbenchLayoutPath));
        Assert.Null((await store.ReadAsync()).Layout);
    }

    [Theory]
    [InlineData("different-format", 1, WorkbenchLayoutStoreFailure.UnsupportedVersion)]
    [InlineData("harness-workbench-layout-v1", 2, WorkbenchLayoutStoreFailure.UnsupportedVersion)]
    [InlineData("harness-workbench-layout-v1", 1, WorkbenchLayoutStoreFailure.IntegrityMismatch)]
    public async Task Rejects_unsupported_or_hash_mismatched_envelopes(
        string format,
        int version,
        WorkbenchLayoutStoreFailure expected)
    {
        StubApplicationPaths paths = new(CreatePaths());
        await WriteEnvelopeAsync(paths, format, version, "{}", new string('0', 64));

        WorkbenchLayoutStoreReadResult result =
            await new FileWorkbenchLayoutStore(paths).ReadAsync();

        Assert.Null(result.Layout);
        Assert.Equal(expected, result.Failure);
    }

    [Fact]
    public async Task Rejects_malformed_unknown_and_oversized_state()
    {
        StubApplicationPaths paths = new(CreatePaths());
        Directory.CreateDirectory(paths.Current.StateDirectory);
        await File.WriteAllTextAsync(paths.Current.WorkbenchLayoutPath, "not json");
        WorkbenchLayoutStoreReadResult malformed =
            await new FileWorkbenchLayoutStore(paths).ReadAsync();
        Assert.Null(malformed.Layout);
        Assert.Equal(WorkbenchLayoutStoreFailure.StorageUnavailable, malformed.Failure);

        await File.WriteAllTextAsync(paths.Current.WorkbenchLayoutPath, """
            {
              "Format": "harness-workbench-layout-v1",
              "Version": 1,
              "Payload": "{}",
              "PayloadSha256": "00",
              "Unexpected": true
            }
            """);
        WorkbenchLayoutStoreReadResult unknown =
            await new FileWorkbenchLayoutStore(paths).ReadAsync();
        Assert.Null(unknown.Layout);
        Assert.Equal(WorkbenchLayoutStoreFailure.StorageUnavailable, unknown.Failure);

        await File.WriteAllTextAsync(
            paths.Current.WorkbenchLayoutPath,
            new string('x', FileWorkbenchLayoutStore.MaximumPayloadBytes + 4097));
        WorkbenchLayoutStoreReadResult oversized =
            await new FileWorkbenchLayoutStore(paths).ReadAsync();
        Assert.Null(oversized.Layout);
        Assert.Equal(WorkbenchLayoutStoreFailure.TooLarge, oversized.Failure);
    }

    [Fact]
    public async Task Rejects_empty_and_oversized_writes_and_honors_cancellation()
    {
        FileWorkbenchLayoutStore store = new(new StubApplicationPaths(CreatePaths()));

        Assert.Equal(
            WorkbenchLayoutStoreFailure.InvalidContent,
            (await store.WriteAsync(new(" "))).Failure);
        Assert.Equal(
            WorkbenchLayoutStoreFailure.TooLarge,
            (await store.WriteAsync(new(new string(
                'x',
                FileWorkbenchLayoutStore.MaximumPayloadBytes + 1)))).Failure);

        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await store.WriteAsync(new("{}"), cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await store.ResetAsync(cancellation.Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private async ValueTask WriteEnvelopeAsync(
        StubApplicationPaths paths,
        string format,
        int version,
        string payload,
        string? hash = null)
    {
        Directory.CreateDirectory(paths.Current.StateDirectory);
        string resolvedHash = hash ?? Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        string json = JsonSerializer.Serialize(new
        {
            Format = format,
            Version = version,
            Payload = payload,
            PayloadSha256 = resolvedHash,
        });
        await File.WriteAllTextAsync(paths.Current.WorkbenchLayoutPath, json);
    }

    private ApplicationPaths CreatePaths() => new(
        Path.Combine(testDirectory, "config"),
        Path.Combine(testDirectory, "data"),
        Path.Combine(testDirectory, "state"),
        Path.Combine(testDirectory, "cache"),
        Path.Combine(testDirectory, "data", "harness.db"),
        Path.Combine(testDirectory, "state", "logs"),
        Path.Combine(testDirectory, "state", "worktrees"));

    private sealed class StubApplicationPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
