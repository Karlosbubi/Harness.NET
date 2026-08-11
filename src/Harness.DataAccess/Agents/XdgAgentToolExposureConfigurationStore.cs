using System.Xml;
using System.Xml.Linq;
using Harness.DataAccess.Configuration;

namespace Harness.DataAccess.Agents;

internal sealed class XdgAgentToolExposureConfigurationStore(IApplicationPaths applicationPaths)
    : IAgentToolExposureConfigurationStore
{
    public AgentToolExposureConfiguration Current { get; private set; } = Load(applicationPaths);

    public async ValueTask<AgentToolExposureConfiguration> SaveAsync(
        AgentToolExposureConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        string directory = applicationPaths.Current.ConfigDirectory;
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "harness.xml");
        XDocument document = File.Exists(path) ? Read(path) : new(new XElement("Harness"));
        document.Root!.Element("AgentTools")?.Remove();
        string[] values = configuration.DirectModuleIds.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        document.Root.Add(new XElement("AgentTools", new XElement("DirectModules",
            values.Select(value => new XElement("Module", value)))));
        string temporary = Path.Combine(directory, $".harness.{Guid.NewGuid():N}.tmp");
        try
        {
            await using FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
            await document.SaveAsync(stream, SaveOptions.None, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return Current = new(values);
    }

    private static AgentToolExposureConfiguration Load(IApplicationPaths paths)
    {
        string path = Path.Combine(paths.Current.ConfigDirectory, "harness.xml");
        if (!File.Exists(path)) return new([]);
        XDocument document = Read(path);
        return new(document.Root?.Element("AgentTools")?.Element("DirectModules")?
            .Elements("Module").Select(item => item.Value.Trim())
            .Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray() ?? []);
    }

    private static XDocument Read(string path)
    {
        XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using XmlReader reader = XmlReader.Create(path, settings);
        XDocument document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        return document.Root?.Name.LocalName == "Harness" ? document :
            throw new InvalidDataException("The user configuration root must be 'Harness'.");
    }
}
