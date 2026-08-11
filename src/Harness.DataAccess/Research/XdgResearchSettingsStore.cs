using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Harness.DataAccess.Configuration;

namespace Harness.DataAccess.Research;

internal sealed class XdgResearchSettingsStore(IApplicationPaths applicationPaths)
    : IResearchSettingsStore
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async ValueTask<ResearchSourceSettings> GetAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return Read(Load(Path()));
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask SaveAsync(
        ResearchSourceSettings settings,
        CancellationToken cancellationToken = default)
    {
        Validate(settings);
        await gate.WaitAsync(cancellationToken);
        try
        {
            XDocument document = Load(Path());
            XElement root = document.Root!;
            root.Element("Research")?.Remove();
            root.Add(Write(settings));
            string directory = applicationPaths.Current.ConfigDirectory;
            Directory.CreateDirectory(directory);
            string temporary = System.IO.Path.Combine(directory,
                $".harness-research.{Guid.NewGuid():N}.tmp");
            try
            {
                await using FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
                await document.SaveAsync(stream, SaveOptions.None, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                File.Move(temporary, Path(), overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static ResearchSourceSettings Read(XDocument document)
    {
        XElement? research = document.Root?.Element("Research");
        ResearchSourceSettings defaults = Defaults();
        if (research is null)
        {
            return defaults;
        }
        ResearchSourceSettings value = defaults with
        {
            ExactLocalEnabled = Bool(research, "ExactLocalEnabled", defaults.ExactLocalEnabled),
            LocalIndexEnabled = Bool(research, "LocalIndexEnabled", defaults.LocalIndexEnabled),
            McpEnabled = Bool(research, "McpEnabled", defaults.McpEnabled),
            WebEnabled = Bool(research, "WebEnabled", defaults.WebEnabled),
            Offline = Bool(research, "Offline", defaults.Offline),
            IndexRoots = research.Element("IndexRoots")?.Elements("Root")
                .Select(element => new DocumentationIndexRoot(element.Value.Trim()))
                .Where(root => root.Value.Length > 0).ToArray() ?? [],
            McpTools = research.Element("McpTools")?.Elements("Tool")
                .Select(element => new DocumentationMcpToolRoute(
                    element.Attribute("Connection")?.Value ?? string.Empty,
                    element.Attribute("Name")?.Value ?? string.Empty))
                .Where(route => route.Connection.Length > 0 && route.Tool.Length > 0).ToArray() ?? [],
            WebEndpoints = research.Element("WebEndpoints")?.Elements("Endpoint")
                .Select(element => Uri.TryCreate(element.Value.Trim(), UriKind.Absolute, out Uri? uri)
                    ? new DocumentationWebEndpoint(uri) : null)
                .OfType<DocumentationWebEndpoint>().ToArray() ?? defaults.WebEndpoints,
            PackageSources = research.Element("PackageSources")?.Elements("Source")
                .Select(element => Uri.TryCreate(element.Value.Trim(), UriKind.Absolute, out Uri? uri)
                    ? new PackageSourceUri(uri) : null)
                .OfType<PackageSourceUri>().ToArray() ?? defaults.PackageSources,
            RefreshPolicy = Enum.TryParse(Text(research, "RefreshPolicy"), true,
                out ResearchRefreshPolicy policy) ? policy : defaults.RefreshPolicy,
            MaximumResults = Int(research, "MaximumResults", defaults.MaximumResults),
            MaximumCharacters = Int(research, "MaximumCharacters", defaults.MaximumCharacters),
            MaximumCacheAge = TimeSpan.FromHours(Int(research, "MaximumCacheAgeHours",
                (int)defaults.MaximumCacheAge.TotalHours)),
            Retention = TimeSpan.FromDays(Int(research, "RetentionDays",
                (int)defaults.Retention.TotalDays)),
        };
        Validate(value);
        return value;
    }

    private static XElement Write(ResearchSourceSettings settings) => new("Research",
        new XElement("ExactLocalEnabled", settings.ExactLocalEnabled),
        new XElement("LocalIndexEnabled", settings.LocalIndexEnabled),
        new XElement("McpEnabled", settings.McpEnabled),
        new XElement("WebEnabled", settings.WebEnabled),
        new XElement("Offline", settings.Offline),
        new XElement("RefreshPolicy", settings.RefreshPolicy),
        new XElement("MaximumResults", settings.MaximumResults),
        new XElement("MaximumCharacters", settings.MaximumCharacters),
        new XElement("MaximumCacheAgeHours", (int)settings.MaximumCacheAge.TotalHours),
        new XElement("RetentionDays", (int)settings.Retention.TotalDays),
        new XElement("IndexRoots", settings.IndexRoots.Select(root => new XElement("Root", root.Value)).ToArray()),
        new XElement("McpTools", settings.McpTools.Select(route => new XElement("Tool",
            new XAttribute("Connection", route.Connection), new XAttribute("Name", route.Tool))).ToArray()),
        new XElement("WebEndpoints", settings.WebEndpoints.Select(endpoint =>
            new XElement("Endpoint", endpoint.Value.AbsoluteUri)).ToArray()),
        new XElement("PackageSources", settings.PackageSources.Select(source =>
            new XElement("Source", source.Value.AbsoluteUri)).ToArray()));

    private static ResearchSourceSettings Defaults() => new(
        ExactLocalEnabled: true,
        LocalIndexEnabled: true,
        McpEnabled: true,
        WebEnabled: true,
        Offline: false,
        IndexRoots: [],
        McpTools: [],
        WebEndpoints: [new(new("https://learn.microsoft.com/api/search"))],
        PackageSources: [new(new("https://api.nuget.org/v3/index.json"))],
        ResearchRefreshPolicy.OnDemand,
        MaximumResults: 5,
        MaximumCharacters: 12_000,
        MaximumCacheAge: TimeSpan.FromDays(7),
        Retention: TimeSpan.FromDays(30));

    private static void Validate(ResearchSourceSettings settings)
    {
        if (settings.MaximumResults is < 1 or > 20 ||
            settings.MaximumCharacters is < 1_000 or > 100_000 ||
            settings.MaximumCacheAge < TimeSpan.Zero || settings.MaximumCacheAge > TimeSpan.FromDays(365) ||
            settings.Retention < TimeSpan.Zero || settings.Retention > TimeSpan.FromDays(3_650) ||
            settings.WebEndpoints.Any(endpoint => !Secure(endpoint.Value)) ||
            settings.PackageSources.Any(source => !Secure(source.Value)))
        {
            throw new InvalidDataException("Research settings contain an invalid limit or endpoint.");
        }
    }

    private static bool Secure(Uri value) => value.Scheme == Uri.UriSchemeHttps || value.IsLoopback;

    private static bool Bool(XElement parent, string name, bool fallback) =>
        bool.TryParse(Text(parent, name), out bool result) ? result : fallback;

    private static int Int(XElement parent, string name, int fallback) =>
        int.TryParse(Text(parent, name), NumberStyles.Integer, CultureInfo.InvariantCulture,
            out int result) ? result : fallback;

    private static string? Text(XElement parent, string name) => parent.Element(name)?.Value.Trim();

    private string Path() => System.IO.Path.Combine(
        applicationPaths.Current.ConfigDirectory, "harness.xml");

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
}
