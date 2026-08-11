using System.Security.Cryptography;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.VisualCapture;

namespace Harness.DataAccess.Tests.VisualCapture;

public sealed class FileVisualCaptureArtifactStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"harness-capture-{Guid.NewGuid():N}");

    [Fact]
    public async Task Stores_reads_and_revokes_exact_private_goal_scoped_bytes()
    {
        FileVisualCaptureArtifactStore store = CreateStore();
        byte[] bytes = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0];
        string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        StoredVisualCapture capture = Capture("goal-a", "11111111111111111111111111111111", bytes.Length, hash);

        StoredVisualCapture stored = await store.StoreAsync(new(capture, bytes));
        StoredVisualCaptureContent? read = await store.ReadAsync(capture.GoalId, capture.Id);

        Assert.Equal(bytes, read?.Content.ToArray());
        Assert.Single(await store.ListAsync(capture.GoalId));
        Assert.Empty(await store.ListAsync("goal-b"));
        Assert.EndsWith(".png", stored.ArtifactFileName, StringComparison.Ordinal);
        Assert.True(await store.DeleteAsync(capture.GoalId, capture.Id));
        Assert.Null(await store.ReadAsync(capture.GoalId, capture.Id));
    }

    [Fact]
    public async Task Cleanup_removes_expired_excess_and_interrupted_artifacts()
    {
        FileVisualCaptureArtifactStore store = CreateStore();
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-10T12:00:00Z");
        byte[] content = [1, 2, 3];
        string hash = Convert.ToHexStringLower(SHA256.HashData(content));
        await store.StoreAsync(new(Capture("goal-a", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 3, hash) with
        { CreatedAt = now.AddDays(-10) }, content));
        await store.StoreAsync(new(Capture("goal-a", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 3, hash) with
        { CreatedAt = now.AddMinutes(-2) }, content));
        await store.StoreAsync(new(Capture("goal-a", "cccccccccccccccccccccccccccccccc", 3, hash) with
        { CreatedAt = now.AddMinutes(-1) }, content));
        string interrupted = Path.Combine(root, "state", "visual-captures", "goal-a", ".interrupted.tmp");
        await File.WriteAllTextAsync(interrupted, "partial");

        VisualCaptureCleanupResult result = await store.CleanupAsync(new(7, 1, now));

        Assert.Equal(2, result.RemovedCaptures);
        Assert.Equal(1, result.RemovedTemporaryFiles);
        Assert.Single(await store.ListAsync("goal-a"));
        Assert.False(File.Exists(interrupted));
    }

    private FileVisualCaptureArtifactStore CreateStore() => new(new StubPaths(new(
        Path.Combine(root, "config"), Path.Combine(root, "data"), Path.Combine(root, "state"),
        Path.Combine(root, "cache"), Path.Combine(root, "data", "harness.db"),
        Path.Combine(root, "state", "logs"), Path.Combine(root, "state", "worktrees"))));

    private static StoredVisualCapture Capture(string goalId, string id, int bytes, string hash) => new(
        new(id), goalId, "workspace-a", "Developer", "Verify UI", "Harness.NET",
        StoredVisualCaptureTarget.UserSelection, StoredVisualCaptureIdentityState.Unavailable,
        null, null, StoredVisualCaptureScaleState.ApplicationSupplied, 1.5, 1, 1,
        "image/png", bytes, hash, DateTimeOffset.UtcNow, string.Empty);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
