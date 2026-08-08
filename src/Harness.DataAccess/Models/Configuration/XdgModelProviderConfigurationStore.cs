using System.Xml;
using System.Xml.Linq;
using Harness.DataAccess.Configuration;

namespace Harness.DataAccess.Models.Configuration;

internal sealed class XdgModelProviderConfigurationStore : IModelProviderConfigurationStore
{
    private readonly IApplicationPaths applicationPaths;
    private readonly Dictionary<string, StoredModelProviderConfiguration> providers;
    private readonly SemaphoreSlim gate = new(1, 1);

    public XdgModelProviderConfigurationStore(
        IApplicationPaths applicationPaths,
        ModelProviderConfigurationOptions options)
    {
        this.applicationPaths = applicationPaths;
        providers = options.Providers.ToDictionary(
            provider => provider.Name.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    public async ValueTask<IReadOnlyList<StoredModelProviderConfiguration>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return providers.Values
                .OrderBy(provider => provider.Kind)
                .ThenBy(provider => provider.Name.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<StoredModelProviderConfiguration> SaveAsync(
        StoredModelProviderConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!providers.TryGetValue(configuration.Name.Value, out StoredModelProviderConfiguration? active) ||
                active.Kind != configuration.Kind)
            {
                throw new InvalidOperationException(
                    $"Provider '{configuration.Name.Value}' is not an active configured module.");
            }

            string directory = applicationPaths.Current.ConfigDirectory;
            string path = Path.Combine(directory, "harness.xml");
            Directory.CreateDirectory(directory);
            XDocument document = Load(path);
            XElement root = document.Root ?? throw new InvalidDataException(
                "The user configuration document has no root element.");
            XElement providerRoot = Child(root, "Providers");
            XElement provider = Child(providerRoot, configuration.Name.Value);
            Set(provider, "Kind", configuration.Kind.ToString());
            Set(provider, "Endpoint", configuration.Endpoint.Value.AbsoluteUri.TrimEnd('/'));
            Set(provider, "ChatModel", configuration.ChatModel.Value);
            Set(provider, "EmbeddingModel", configuration.EmbeddingModel.Value);
            Set(provider, "EmbeddingDimensions", configuration.EmbeddingDimensions.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            Set(provider, "ConnectTimeoutSeconds", ((int)configuration.ConnectTimeout.Value.TotalSeconds)
                .ToString(System.Globalization.CultureInfo.InvariantCulture));
            Set(provider, "RequestTimeoutSeconds", ((int)configuration.RequestTimeout.Value.TotalSeconds)
                .ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (configuration.ApiKeyReference is { } secret)
            {
                Set(provider, "ApiKeySecret", secret.Name);
                if (secret.EnvironmentVariable is { Length: > 0 } environmentVariable)
                {
                    Set(provider, "ApiKeyEnvironmentVariable", environmentVariable);
                }
                else
                {
                    provider.Element("ApiKeyEnvironmentVariable")?.Remove();
                }
            }

            string temporaryPath = Path.Combine(directory, $".harness.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (FileStream stream = new(
                                 temporaryPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 4096,
                                 FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await document.SaveAsync(stream, SaveOptions.None, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            StoredModelProviderConfiguration saved = configuration with { RequiresRestart = true };
            providers[configuration.Name.Value] = saved;
            return saved;
        }
        finally
        {
            gate.Release();
        }
    }

    private static XDocument Load(string path)
    {
        if (!File.Exists(path))
        {
            return new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement("Harness"));
        }

        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        using XmlReader reader = XmlReader.Create(path, settings);
        XDocument document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (document.Root?.Name.LocalName != "Harness")
        {
            throw new InvalidDataException("The user configuration root must be 'Harness'.");
        }

        return document;
    }

    private static XElement Child(XElement parent, string name)
    {
        XElement? child = parent.Element(name);
        if (child is not null)
        {
            return child;
        }

        child = new XElement(name);
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
