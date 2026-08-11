using System.Text.Json;
using Harness.DataAccess.Configuration;

namespace Harness.DataAccess.Mcp;

internal sealed class FileInboundMcpAuditStore(IApplicationPaths paths) : IInboundMcpAuditStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask AppendAsync(InboundMcpAuditRecord record, int retention,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            string path = Path();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            List<InboundMcpAuditRecord> records = await ReadAsync(path, cancellationToken);
            records.Add(record);
            InboundMcpAuditRecord[] retained = retention == 0 ? [] : records.TakeLast(retention).ToArray();
            string temporary = path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await File.WriteAllLinesAsync(temporary,
                    retained.Select(item => JsonSerializer.Serialize(item, JsonOptions)), cancellationToken);
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { gate.Release(); }
    }

    public async ValueTask<IReadOnlyList<InboundMcpAuditRecord>> ListAsync(
        int maximumResults, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return (await ReadAsync(Path(), cancellationToken)).TakeLast(Math.Clamp(maximumResults, 1, 500))
              .Reverse<InboundMcpAuditRecord>().ToArray();
        }
        finally { gate.Release(); }
    }

    private string Path() => System.IO.Path.Combine(paths.Current.StateDirectory, "inbound-mcp-audit.jsonl");
    private static async ValueTask<List<InboundMcpAuditRecord>> ReadAsync(
        string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return [];
        List<InboundMcpAuditRecord> records = [];
        foreach (string line in await File.ReadAllLinesAsync(path, cancellationToken))
        {
            try
            {
                InboundMcpAuditRecord? record = JsonSerializer.Deserialize<InboundMcpAuditRecord>(line, JsonOptions);
                if (record is not null) records.Add(record);
            }
            catch (JsonException) { }
        }
        return records;
    }
}
