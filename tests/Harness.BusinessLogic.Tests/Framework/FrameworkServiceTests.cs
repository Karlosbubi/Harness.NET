using Harness.BusinessLogic.Framework;
using Harness.DataAccess.Framework;

namespace Harness.BusinessLogic.Tests.Framework;

public sealed class FrameworkServiceTests
{
    [Fact]
    public async Task Composes_file_private_and_typed_framework_layers()
    {
        FakeOverlayStore overlays = new()
        {
            Overlay = new("workspace-id", "Private guidance", DateTimeOffset.UtcNow),
        };
        FrameworkService service = new(
            new FakeSourceReader(new(
                [
                    new("global", 0, "/config/framework.md", "Global guidance", true),
                    new("repository", 1, "/repo/AGENTS.md", "Repository guidance", false),
                ],
                [])),
            overlays,
            new FrameworkResolver(),
            new([
                new("testing", "xunit", 1, "repository", false, "harness.xml"),
            ]));

        FrameworkSnapshot snapshot = await service.GetEffectiveAsync(
            "workspace-id",
            "/repo");

        Assert.Equal(3, snapshot.Documents.Count);
        Assert.Equal("private-workspace", snapshot.Documents[2].Layer);
        Assert.True(snapshot.Documents[2].IsPrivate);
        Assert.Equal("xunit", Assert.Single(snapshot.Rules).Value);
        Assert.True(snapshot.IsValid);
    }

    [Fact]
    public async Task Empty_private_overlay_deletes_existing_content()
    {
        FakeOverlayStore overlays = new()
        {
            Overlay = new("workspace-id", "Private guidance", DateTimeOffset.UtcNow),
        };
        FrameworkService service = new(
            new FakeSourceReader(new([], [])),
            overlays,
            new FrameworkResolver(),
            new([]));

        FrameworkSnapshot snapshot = await service.SetPrivateOverlayAsync(
            "workspace-id",
            "/repo",
            "  ");

        Assert.True(overlays.WasDeleted);
        Assert.Empty(snapshot.Documents);
    }

    [Fact]
    public async Task Source_failure_invalidates_the_effective_snapshot()
    {
        FrameworkService service = new(
            new FakeSourceReader(new([], ["Could not read AGENTS.md"])),
            new FakeOverlayStore(),
            new FrameworkResolver(),
            new([]));

        FrameworkSnapshot snapshot = await service.GetEffectiveAsync(
            "workspace-id",
            "/repo");

        Assert.False(snapshot.IsValid);
        Assert.Equal("source_error", Assert.Single(snapshot.Issues).Code);
    }

    private sealed class FakeSourceReader(FrameworkSourceResult result)
        : IFrameworkSourceReader
    {
        public ValueTask<FrameworkSourceResult> ReadAsync(
            string workspaceRoot,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(result);
    }

    private sealed class FakeOverlayStore : IFrameworkOverlayStore
    {
        internal WorkspaceFrameworkOverlay? Overlay { get; set; }
        internal bool WasDeleted { get; private set; }

        public ValueTask<WorkspaceFrameworkOverlay?> GetAsync(
            string workspaceId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Overlay);

        public ValueTask<WorkspaceFrameworkOverlay> SaveAsync(
            string workspaceId,
            string content,
            CancellationToken cancellationToken = default)
        {
            Overlay = new(workspaceId, content, DateTimeOffset.UtcNow);
            return ValueTask.FromResult(Overlay);
        }

        public ValueTask DeleteAsync(
            string workspaceId,
            CancellationToken cancellationToken = default)
        {
            Overlay = null;
            WasDeleted = true;
            return ValueTask.CompletedTask;
        }
    }
}
