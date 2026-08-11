using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Harness.DataAccess.Configuration;

namespace Harness.DataAccess.Mcp;

internal sealed class XdgInboundMcpSettingsStore(IApplicationPaths applicationPaths)
    : IInboundMcpSettingsStore
{
    private static readonly HashSet<string> KnownTools = new(StringComparer.Ordinal)
    {
        "harness_application", "harness_workspace", "harness_tree", "harness_read_range",
        "harness_git", "harness_project_graph", "harness_goals", "harness_evidence",
        "harness_decide_plan",
        "harness_build", "harness_test", "harness_ui", "harness_ui_activate",
        "harness_open_document",
        "harness_request_capture", "harness_inspect_capture", "harness_audit",
        "harness_code_problems", "harness_code_symbol", "harness_code_definition",
        "harness_code_references", "harness_code_implementations",
        "harness_evaluation_snapshot", "harness_evaluation_reset",
    };
    internal static readonly InboundMcpServerSettings Default = new(
        IsEnabled: false,
        InboundMcpMode.Normal,
        new("http://127.0.0.1:57431/mcp"),
        new("inbound-mcp-bearer-token"),
        [],
        [new("harness_application"), new("harness_workspace"), new("harness_tree"),
            new("harness_read_range"), new("harness_git"), new("harness_project_graph"),
            new("harness_goals"), new("harness_evidence"), new("harness_ui"),
            new("harness_open_document"), new("harness_audit"),
            new("harness_evaluation_snapshot"),
            new("harness_code_problems"), new("harness_code_symbol"),
            new("harness_code_definition"), new("harness_code_references"),
            new("harness_code_implementations")],
        // Build and Test are known but remain disabled until the user adds them explicitly.
        [],
        new(TimeSpan.FromSeconds(30)),
        new(500),
        new(1_000),
        RequiresRestart: false);

    public ValueTask<InboundMcpServerSettings> GetAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = Path.Combine(applicationPaths.Current.ConfigDirectory, "harness.xml");
        if (!File.Exists(path)) return ValueTask.FromResult(Default);
        XDocument document = Load(path);
        XElement? node = document.Root?.Element("InboundMcp");
        if (node is null) return ValueTask.FromResult(Default);
        InboundMcpServerSettings settings = Default with
        {
            IsEnabled = Bool(node, "Enabled", Default.IsEnabled),
            Mode = Enum.TryParse(node.Element("Mode")?.Value, true, out InboundMcpMode mode)
                ? mode : Default.Mode,
            Endpoint = Uri.TryCreate(node.Element("Endpoint")?.Value, UriKind.Absolute, out Uri? endpoint)
                ? endpoint : Default.Endpoint,
            AllowedClients = Values(node, "AllowedClients", "Client", value => new InboundMcpClientId(value)),
            AllowedTools = Values(node, "AllowedTools", "Tool", value => new InboundMcpToolId(value)),
            ApprovalRequiredTools = Values(node, "ApprovalRequiredTools", "Tool", value => new InboundMcpToolId(value)),
            RequestTimeout = new(TimeSpan.FromSeconds(Int(node, "RequestTimeoutSeconds", 30))),
            ResultLimit = new(Int(node, "ResultLimit", 500)),
            AuditRetention = new(Int(node, "AuditRetention", 1_000)),
        };
        return ValueTask.FromResult(settings);
    }

    public async ValueTask<InboundMcpServerSettings> SaveAsync(
        InboundMcpServerSettings settings, CancellationToken cancellationToken = default)
    {
        Validate(settings);
        string directory = applicationPaths.Current.ConfigDirectory;
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "harness.xml");
        XDocument document = File.Exists(path) ? Load(path) : new(new XElement("Harness"));
        XElement root = document.Root!;
        root.Element("InboundMcp")?.Remove();
        root.Add(new XElement("InboundMcp",
            new XElement("Enabled", settings.IsEnabled),
            new XElement("Mode", settings.Mode),
            new XElement("Endpoint", settings.Endpoint.AbsoluteUri),
            new XElement("RequestTimeoutSeconds", (int)settings.RequestTimeout.Value.TotalSeconds),
            new XElement("ResultLimit", settings.ResultLimit.Value),
            new XElement("AuditRetention", settings.AuditRetention.Value),
            new XElement("AllowedClients", settings.AllowedClients.Select(item => new XElement("Client", item.Value))),
            new XElement("AllowedTools", settings.AllowedTools.Select(item => new XElement("Tool", item.Value))),
            new XElement("ApprovalRequiredTools", settings.ApprovalRequiredTools.Select(item => new XElement("Tool", item.Value)))));
        string temporary = Path.Combine(directory, $".harness.{Guid.NewGuid():N}.tmp");
        try
        {
            await using FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
            await document.SaveAsync(stream, SaveOptions.None, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return settings with { RequiresRestart = true };
    }

    internal static void Validate(InboundMcpServerSettings settings)
    {
        if (settings.Endpoint.Scheme != Uri.UriSchemeHttp || !settings.Endpoint.IsLoopback ||
            settings.Endpoint.Port is < 1024 or > 65535 ||
            settings.Endpoint.AbsolutePath is "/" or "" ||
            settings.RequestTimeout.Value < TimeSpan.FromSeconds(1) ||
            settings.RequestTimeout.Value > TimeSpan.FromMinutes(5) ||
            settings.ResultLimit.Value is < 1 or > 5_000 ||
            settings.AuditRetention.Value is < 0 or > 100_000)
        {
            throw new ArgumentException("Inbound MCP requires a bounded HTTP loopback endpoint and valid limits.", nameof(settings));
        }
        string? unknown = settings.AllowedTools.Concat(settings.ApprovalRequiredTools)
            .Select(item => item.Value).FirstOrDefault(value => !KnownTools.Contains(value));
        if (unknown is not null)
            throw new ArgumentException($"Unknown inbound MCP tool ID: {unknown}", nameof(settings));
        if (settings.AllowedClients.Any(item => string.IsNullOrWhiteSpace(item.Value) || item.Value.Length > 128))
            throw new ArgumentException("Inbound MCP client IDs must contain 1–128 characters.", nameof(settings));
    }

    private static XDocument Load(string path)
    {
        XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using XmlReader reader = XmlReader.Create(path, settings);
        XDocument document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        return document.Root?.Name.LocalName == "Harness" ? document :
            throw new InvalidDataException("The user configuration root must be 'Harness'.");
    }

    private static bool Bool(XElement node, string name, bool fallback) =>
        bool.TryParse(node.Element(name)?.Value, out bool value) ? value : fallback;
    private static int Int(XElement node, string name, int fallback) =>
        int.TryParse(node.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value : fallback;
    private static IReadOnlyList<T> Values<T>(XElement node, string parent, string child, Func<string, T> map) =>
        node.Element(parent)?.Elements(child).Select(item => item.Value.Trim()).Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal).Select(map).ToArray() ?? [];
}
