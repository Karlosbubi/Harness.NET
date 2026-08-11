using System.Xml;
using System.Xml.Linq;
using Harness.DataAccess.Configuration;

namespace Harness.DataAccess.Mcp;

internal sealed class XdgMcpConnectionConfigurationStore : IMcpConnectionConfigurationStore
{
    private readonly IApplicationPaths applicationPaths;
    private readonly Dictionary<string, McpConnectionConfiguration> connections;
    private readonly SemaphoreSlim gate = new(1, 1);

    public XdgMcpConnectionConfigurationStore(
        IApplicationPaths applicationPaths,
        McpConnectionConfigurationOptions options)
    {
        this.applicationPaths = applicationPaths;
        connections = options.Connections.ToDictionary(
            connection => connection.Name.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    public async ValueTask<IReadOnlyList<McpConnectionConfiguration>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return connections.Values
                .OrderBy(connection => connection.Name.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<McpConnectionConfiguration> SaveAsync(
        McpConnectionConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            XDocument document = Load(UserConfigurationPath());
            XElement root = document.Root!;
            XElement connectionRoot = Child(root, "McpConnections");
            XElement connection = connectionRoot.Elements()
                .FirstOrDefault(item => item.Name.LocalName.Equals(
                    configuration.Name.Value, StringComparison.OrdinalIgnoreCase))
                ?? new XElement(configuration.Name.Value);
            if (connection.Parent is null)
            {
                connectionRoot.Add(connection);
            }

            Set(connection, "Endpoint", configuration.Endpoint.Value.AbsoluteUri);
            Set(connection, "RequestTimeoutSeconds", ((int)configuration.RequestTimeout.Value.TotalSeconds)
                .ToString(System.Globalization.CultureInfo.InvariantCulture));
            Set(connection, "Enabled", configuration.IsEnabled.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            Set(connection, "Access", configuration.Access.ToString());
            Set(connection, "ClientId", configuration.ClientId?.Value ?? string.Empty);
            Set(connection, "BearerTokenReference",
                configuration.BearerTokenReference?.Value ?? string.Empty);
            Set(connection, "AllowedTools", string.Join(Environment.NewLine,
                (configuration.AllowedTools ?? []).Select(tool => tool.Value)));
            await SaveAsync(document, cancellationToken);

            McpConnectionConfiguration saved = configuration with { RequiresRestart = true };
            connections[configuration.Name.Value] = saved;
            return saved;
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<bool> DeleteAsync(
        McpConnectionName name,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!connections.ContainsKey(name.Value))
            {
                return false;
            }

            XDocument document = Load(UserConfigurationPath());
            XElement? connection = document.Root?.Element("McpConnections")?.Elements()
                .FirstOrDefault(item => item.Name.LocalName.Equals(
                    name.Value, StringComparison.OrdinalIgnoreCase));
            connection?.Remove();
            await SaveAsync(document, cancellationToken);
            connections.Remove(name.Value);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    private string UserConfigurationPath() =>
        Path.Combine(applicationPaths.Current.ConfigDirectory, "harness.xml");

    private async ValueTask SaveAsync(XDocument document, CancellationToken cancellationToken)
    {
        string directory = applicationPaths.Current.ConfigDirectory;
        Directory.CreateDirectory(directory);
        string path = UserConfigurationPath();
        string temporaryPath = Path.Combine(directory, $".harness.{Guid.NewGuid():N}.tmp");
        try
        {
            await using FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await document.SaveAsync(stream, SaveOptions.None, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static XDocument Load(string path)
    {
        if (!File.Exists(path))
        {
            return new(new XDeclaration("1.0", "utf-8", null), new XElement("Harness"));
        }

        XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using XmlReader reader = XmlReader.Create(path, settings);
        XDocument document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        return document.Root?.Name.LocalName == "Harness"
            ? document
            : throw new InvalidDataException("The user configuration root must be 'Harness'.");
    }

    private static XElement Child(XElement parent, string name)
    {
        XElement? child = parent.Element(name);
        if (child is not null)
        {
            return child;
        }

        child = new(name);
        parent.Add(child);
        return child;
    }

    private static void Set(XElement parent, string name, string value)
    {
        XElement? child = parent.Element(name);
        if (child is null)
        {
            parent.Add(new XElement(name, value));
        }
        else
        {
            child.Value = value;
        }
    }
}
