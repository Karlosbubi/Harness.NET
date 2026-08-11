using Harness.DataAccess.Configuration;
using Harness.DataAccess.Mcp;

namespace Harness.DataAccess.Tests.Mcp;

public sealed class FileInboundMcpAuditStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(),
        "harness-inbound-audit", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Retains_a_bounded_newest_first_log_without_arguments_or_results()
    {
        FileInboundMcpAuditStore store = new(new Paths(root));
        for (int index = 0; index < 3; index++)
        {
            await store.AppendAsync(new(index.ToString(), new("instance"), new("client"),
                new("harness_workspace"), InboundMcpMode.Normal,
                InboundMcpAuditOutcome.Succeeded, DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddSeconds(index), null), retention: 2);
        }

        IReadOnlyList<InboundMcpAuditRecord> records = await store.ListAsync(10);

        Assert.Equal(["2", "1"], records.Select(item => item.Id));
        string persisted = await File.ReadAllTextAsync(
            Path.Combine(root, "inbound-mcp-audit.jsonl"));
        Assert.DoesNotContain("arguments", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("resultJson", persisted, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class Paths(string root) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = new(root, root, root, root,
            Path.Combine(root, "harness.db"), Path.Combine(root, "logs"),
            Path.Combine(root, "worktrees"));
    }
}
