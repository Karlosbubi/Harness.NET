using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Harness.DataAccess.Research;

internal sealed class LocalPackageDocumentationSource(TimeProvider timeProvider)
    : IDocumentationSource
{
    private const int MaximumFiles = 300;
    private const long MaximumFileBytes = 4 * 1024 * 1024;
    private static readonly IReadOnlyDictionary<string, string[]> LibraryPackages =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [".NET"] = ["Microsoft.NETCore.App.Ref", "NETStandard.Library.Ref"],
            ["Avalonia"] = ["Avalonia", "Avalonia.Desktop"],
            ["Rx.NET"] = ["System.Reactive"],
            ["System.Reactive"] = ["System.Reactive"],
            ["Serilog"] = ["Serilog"],
            ["Microsoft Agent Framework"] = ["Microsoft.Agents.AI"],
            ["Microsoft.Agents.AI"] = ["Microsoft.Agents.AI"],
            ["Roslyn"] = ["Microsoft.CodeAnalysis.Common", "Microsoft.CodeAnalysis.CSharp"],
            ["Dock"] = ["Dock.Avalonia", "Dock.Model.Avalonia"],
            ["Dapper"] = ["Dapper"],
            ["SQLite"] = ["Microsoft.Data.Sqlite"],
            ["xUnit"] = ["xunit", "xunit.v3.core"],
        };

    public DocumentationSourceId Id { get; } = new("exact-local-package-docs");

    public DocumentationSourceClass SourceClass => DocumentationSourceClass.ExactLocal;

    public async ValueTask<DocumentationSourceResult> SearchAsync(
        DocumentationSourceQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.Version is null || string.IsNullOrWhiteSpace(query.Version.Value))
        {
            return new(Id, SourceClass, [], false, "documentation_version_required",
                "Exact local documentation requires a library version.");
        }

        string[] packages = LibraryPackages.TryGetValue(query.Library.Value, out string[]? aliases)
            ? aliases
            : [query.Library.Value];
        List<DocumentationSourceMatch> matches = [];
        foreach ((string package, string root, string actualVersion) in
                 LocatePackages(packages, query.Version.Value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SearchRootAsync(
                root,
                $"nuget:{package}@{actualVersion}",
                new(actualVersion),
                query,
                matches,
                cancellationToken);
            if (matches.Count >= query.MaximumResults)
            {
                break;
            }
        }

        DocumentationSourceMatch[] ranked = DocumentationFileSearch.Rank(
            matches, query.MaximumResults, query.MaximumCharacters);
        return new(Id, SourceClass, ranked,
            ranked.Any(match => match.IsExactVersion && match.Confidence >= 0.75m),
            ranked.Length == 0 ? "exact_documentation_unavailable" : null,
            ranked.Length == 0
                ? "No matching documentation was found in the exact restored package or SDK reference pack."
                : null);
    }

    private async ValueTask SearchRootAsync(
        string root,
        string citationPrefix,
        DocumentationVersion actualVersion,
        DocumentationSourceQuery query,
        ICollection<DocumentationSourceMatch> matches,
        CancellationToken cancellationToken)
    {
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MaxRecursionDepth = 8,
        };
        foreach (string file in Directory.EnumerateFiles(root, "*", options)
                     .Where(DocumentationFileSearch.Supported)
                     .Order(StringComparer.Ordinal)
                     .Take(MaximumFiles))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileInfo info = new(file);
            if (info.Length is <= 0 or > MaximumFileBytes)
            {
                continue;
            }
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            IReadOnlyList<DocumentationFileHit> hits = await DocumentationFileSearch.ReadAsync(
                file, query.Query.Value, cancellationToken);
            foreach (DocumentationFileHit hit in hits)
            {
                matches.Add(new(
                    Id,
                    SourceClass,
                    hit.Title,
                    hit.Content,
                    actualVersion,
                    new($"{citationPrefix}/{relative}{hit.Fragment}"),
                    timeProvider.GetUtcNow(),
                    DocumentationFileSearch.Sha256(hit.Content),
                    IsExactVersion: VersionMatches(query.Version?.Value, actualVersion.Value),
                    IsStale: false,
                    hit.Confidence));
            }
        }
    }

    private static bool VersionMatches(string? requested, string actual) =>
        requested is not null &&
        (actual.Equals(requested, StringComparison.OrdinalIgnoreCase) ||
         actual.StartsWith(requested.TrimEnd('.') + ".", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<(string Package, string Root, string ActualVersion)> LocatePackages(
        IEnumerable<string> packages,
        string version)
    {
        string global = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget", "packages");
        foreach (string package in packages)
        {
            string candidate = Path.Combine(global, package.ToLowerInvariant(), version.ToLowerInvariant());
            if (Directory.Exists(candidate))
            {
                yield return (package, candidate, version);
            }
        }
        if (packages.Contains("Microsoft.NETCore.App.Ref", StringComparer.OrdinalIgnoreCase))
        {
            string? dotnet = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            dotnet ??= Directory.Exists("/usr/share/dotnet") ? "/usr/share/dotnet" : null;
            if (dotnet is not null)
            {
                string packs = Path.Combine(dotnet, "packs", "Microsoft.NETCore.App.Ref", version);
                if (Directory.Exists(packs))
                {
                    yield return ("Microsoft.NETCore.App.Ref", packs, version);
                }
                else
                {
                    string packRoot = Path.Combine(dotnet, "packs", "Microsoft.NETCore.App.Ref");
                    string prefix = string.Join('.', version.Split('.').Take(2)) + ".";
                    string? compatible = Directory.Exists(packRoot)
                        ? Directory.EnumerateDirectories(packRoot)
                            .Select(Path.GetFileName)
                            .Where(item => item is not null && item.StartsWith(prefix,
                                StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(item => item, StringComparer.OrdinalIgnoreCase)
                            .FirstOrDefault()
                        : null;
                    if (compatible is not null)
                    {
                        yield return ("Microsoft.NETCore.App.Ref",
                            Path.Combine(packRoot, compatible), compatible);
                    }
                }
            }
        }
    }
}

internal sealed class LocalDocumentationIndexSource(
    IResearchSettingsStore settingsStore,
    TimeProvider timeProvider) : IDocumentationSource
{
    private const int MaximumFilesPerRoot = 500;
    private const long MaximumFileBytes = 4 * 1024 * 1024;

    public DocumentationSourceId Id { get; } = new("local-documentation-index");

    public DocumentationSourceClass SourceClass => DocumentationSourceClass.LocalIndex;

    public async ValueTask<DocumentationSourceResult> SearchAsync(
        DocumentationSourceQuery query,
        CancellationToken cancellationToken = default)
    {
        ResearchSourceSettings settings = await settingsStore.GetAsync(cancellationToken);
        List<DocumentationSourceMatch> matches = [];
        foreach (DocumentationIndexRoot configured in settings.IndexRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string root;
            try
            {
                root = Path.GetFullPath(configured.Value);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                continue;
            }
            if (!Directory.Exists(root))
            {
                continue;
            }
            EnumerationOptions options = new()
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                MaxRecursionDepth = 12,
            };
            foreach (string file in Directory.EnumerateFiles(root, "*", options)
                         .Where(DocumentationFileSearch.Supported)
                         .Order(StringComparer.Ordinal)
                         .Take(MaximumFilesPerRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileInfo info = new(file);
                if (info.Length is <= 0 or > MaximumFileBytes)
                {
                    continue;
                }
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                bool exact = query.Version is not null && relative.Split('/')
                    .Any(segment => segment.Equals(query.Version.Value, StringComparison.OrdinalIgnoreCase));
                IReadOnlyList<DocumentationFileHit> hits = await DocumentationFileSearch.ReadAsync(
                    file, query.Query.Value, cancellationToken);
                matches.AddRange(hits.Select(hit => new DocumentationSourceMatch(
                    Id,
                    SourceClass,
                    hit.Title,
                    hit.Content,
                    exact ? query.Version : null,
                    new($"doc-index:{Path.GetFileName(root)}/{relative}{hit.Fragment}"),
                    timeProvider.GetUtcNow(),
                    DocumentationFileSearch.Sha256(hit.Content),
                    exact,
                    IsStale: false,
                    exact ? Math.Min(0.95m, hit.Confidence + 0.1m) : hit.Confidence)));
            }
        }
        DocumentationSourceMatch[] ranked = DocumentationFileSearch.Rank(
            matches, query.MaximumResults, query.MaximumCharacters);
        return new(Id, SourceClass, ranked,
            ranked.Any(match => match.IsExactVersion && match.Confidence >= 0.7m),
            ranked.Length == 0 ? "local_index_no_match" : null,
            ranked.Length == 0 ? "No configured local documentation index matched the query." : null);
    }
}

internal sealed record DocumentationFileHit(
    string Title,
    string Content,
    string Fragment,
    decimal Confidence);

internal static partial class DocumentationFileSearch
{
    private const int MaximumHitsPerFile = 8;
    private const int MaximumHitCharacters = 2_000;
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "does", "for", "from", "how",
        "in", "is", "it", "of", "on", "or", "the", "to", "use", "what", "when", "with",
    };

    internal static bool Supported(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".md" or ".markdown" or ".txt" or ".xml";

    internal static async ValueTask<IReadOnlyList<DocumentationFileHit>> ReadAsync(
        string path,
        string query,
        CancellationToken cancellationToken)
    {
        string[] terms = Terms(query);
        if (terms.Length == 0)
        {
            return [];
        }
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension == ".xml"
            ? await ReadXmlAsync(path, terms, cancellationToken)
            : await ReadTextAsync(path, terms, cancellationToken);
    }

    internal static DocumentationSourceMatch[] Rank(
        IEnumerable<DocumentationSourceMatch> matches,
        int maximumResults,
        int maximumCharacters)
    {
        List<DocumentationSourceMatch> output = [];
        int characters = 0;
        foreach (DocumentationSourceMatch match in matches
                     .GroupBy(item => (item.Citation.Value.ToLowerInvariant(),
                         item.Version?.Value.ToLowerInvariant(), item.ContentSha256))
                     .Select(group => group.OrderByDescending(item => item.Confidence).First())
                     .OrderByDescending(item => item.IsExactVersion)
                     .ThenByDescending(item => item.Confidence)
                     .ThenBy(item => item.Citation.Value, StringComparer.Ordinal))
        {
            if (output.Count >= maximumResults || characters >= maximumCharacters)
            {
                break;
            }
            int remaining = maximumCharacters - characters;
            string content = match.Content.Length <= remaining
                ? match.Content
                : match.Content[..remaining];
            if (content.Length == 0)
            {
                break;
            }
            output.Add(match with { Content = content, ContentSha256 = Sha256(content) });
            characters += content.Length;
        }
        return output.ToArray();
    }

    internal static string Sha256(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static async ValueTask<IReadOnlyList<DocumentationFileHit>> ReadTextAsync(
        string path,
        IReadOnlyList<string> terms,
        CancellationToken cancellationToken)
    {
        string content = await File.ReadAllTextAsync(path, cancellationToken);
        string[] blocks = ParagraphBreak().Split(content);
        return blocks.Select((block, index) => Hit(
                Path.GetFileName(path), block.Trim(), $"#block-{index + 1}", terms))
            .OfType<DocumentationFileHit>()
            .OrderByDescending(hit => hit.Confidence)
            .Take(MaximumHitsPerFile)
            .ToArray();
    }

    private static async ValueTask<IReadOnlyList<DocumentationFileHit>> ReadXmlAsync(
        string path,
        IReadOnlyList<string> terms,
        CancellationToken cancellationToken)
    {
        XmlReaderSettings settings = new()
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 4 * 1024 * 1024,
        };
        await using FileStream stream = File.OpenRead(path);
        using XmlReader reader = XmlReader.Create(stream, settings);
        XDocument document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
        return document.Descendants()
            .Where(element => element.Name.LocalName == "member")
            .Select(element =>
            {
                string name = element.Attribute("name")?.Value ?? Path.GetFileName(path);
                return Hit(name, $"{name}\n{element.Value.Trim()}",
                    $"#member-{Uri.EscapeDataString(name)}", terms);
            })
            .OfType<DocumentationFileHit>()
            .OrderByDescending(hit => hit.Confidence)
            .Take(MaximumHitsPerFile)
            .ToArray();
    }

    private static DocumentationFileHit? Hit(
        string title,
        string content,
        string fragment,
        IReadOnlyList<string> terms)
    {
        if (content.Length == 0)
        {
            return null;
        }
        int matched = terms.Count(term => content.Contains(term, StringComparison.OrdinalIgnoreCase));
        if (matched == 0)
        {
            return null;
        }
        decimal confidence = Math.Min(0.9m, 0.45m + 0.45m * matched / terms.Count);
        string bounded = content.Length <= MaximumHitCharacters
            ? content
            : content[..MaximumHitCharacters];
        return new(title, bounded, fragment, confidence);
    }

    private static string[] Terms(string query) => Word().Matches(query)
        .Select(match => match.Value.ToLowerInvariant())
        .Where(value => value.Length >= 2)
        .Where(value => !StopWords.Contains(value))
        .Distinct(StringComparer.Ordinal)
        .Take(16)
        .ToArray();

    [GeneratedRegex("[A-Za-z0-9_.+-]+", RegexOptions.CultureInvariant)]
    private static partial Regex Word();

    [GeneratedRegex("(?:\\r?\\n){2,}", RegexOptions.CultureInvariant)]
    private static partial Regex ParagraphBreak();
}
