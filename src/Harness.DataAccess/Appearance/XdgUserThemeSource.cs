using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Harness.DataAccess.Configuration;

namespace Harness.DataAccess.Appearance;

internal sealed partial class XdgUserThemeSource(IApplicationPaths applicationPaths)
    : IUserThemeSource
{
    private const int MaximumFiles = 64;
    private const long MaximumFileBytes = 64 * 1024;

    public async ValueTask<UserThemeCatalog> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        string directory = Path.Combine(applicationPaths.Current.ConfigDirectory, "themes");
        if (!Directory.Exists(directory))
        {
            return new([], []);
        }

        string[] files = Directory.EnumerateFiles(directory, "*.xml", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();
        List<UserThemeDefinition> themes = [];
        List<UserThemeIssue> issues = [];
        if (files.Length > MaximumFiles)
        {
            issues.Add(new("themes", $"Only the first {MaximumFiles} theme files were read."));
        }

        HashSet<string> identifiers = new(StringComparer.Ordinal);
        foreach (string file in files.Take(MaximumFiles))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string sourceName = Path.GetFileName(file);
            try
            {
                FileInfo info = new(file);
                if (info.Length > MaximumFileBytes)
                {
                    throw new InvalidDataException("Theme file exceeds 64 KiB.");
                }

                UserThemeDefinition theme = await ReadFileAsync(file, cancellationToken);
                if (!identifiers.Add(theme.Id.Value))
                {
                    throw new InvalidDataException($"Duplicate theme id '{theme.Id.Value}'.");
                }

                themes.Add(theme);
            }
            catch (Exception exception) when (exception is IOException or
                                               UnauthorizedAccessException or
                                               XmlException or
                                               InvalidDataException)
            {
                issues.Add(new(sourceName, exception.Message));
            }
        }

        return new(themes, issues);
    }

    private static async ValueTask<UserThemeDefinition> ReadFileAsync(
        string file,
        CancellationToken cancellationToken)
    {
        XmlReaderSettings settings = new()
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumFileBytes,
            IgnoreComments = true,
            IgnoreWhitespace = true,
        };
        await using FileStream stream = new(
            file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        using XmlReader reader = XmlReader.Create(stream, settings);
        XDocument document = await XDocument.LoadAsync(
            reader, LoadOptions.None, cancellationToken);
        XElement root = document.Root
            ?? throw new InvalidDataException("Theme document has no root element.");
        if (root.Name.LocalName != "harnessTheme" ||
            root.Attribute("version")?.Value != "1")
        {
            throw new InvalidDataException("Theme root must be harnessTheme version 1.");
        }

        string id = RequiredAttribute(root, "id");
        if (!ThemeIdPattern().IsMatch(id))
        {
            throw new InvalidDataException("Theme id is invalid.");
        }

        string name = RequiredAttribute(root, "name");
        if (name.Length > 80)
        {
            throw new InvalidDataException("Theme name exceeds 80 characters.");
        }

        ThemeBaseVariant variant = RequiredAttribute(root, "base") switch
        {
            "light" => ThemeBaseVariant.Light,
            "dark" => ThemeBaseVariant.Dark,
            _ => throw new InvalidDataException("Theme base must be light or dark."),
        };
        Dictionary<ThemeColorToken, ThemeColorValue> colors = [];
        foreach (XElement color in root.Elements())
        {
            if (color.Name.LocalName != "color" || color.HasElements)
            {
                throw new InvalidDataException("Only empty color elements are allowed.");
            }

            if (!Enum.TryParse(RequiredAttribute(color, "token"), ignoreCase: false,
                    out ThemeColorToken token) || !Enum.IsDefined(token))
            {
                throw new InvalidDataException("Theme contains an unknown color token.");
            }

            string value = RequiredAttribute(color, "value");
            if (!ColorPattern().IsMatch(value))
            {
                throw new InvalidDataException($"Color '{value}' must be opaque #RRGGBB.");
            }

            if (!colors.TryAdd(token, new(value)))
            {
                throw new InvalidDataException($"Color token '{token}' is duplicated.");
            }

            if (color.Attributes().Count() != 2)
            {
                throw new InvalidDataException("Color elements accept only token and value.");
            }
        }

        if (root.Attributes().Count() != 4)
        {
            throw new InvalidDataException("Theme root contains unsupported attributes.");
        }

        return new(new(id), name, variant, colors);
    }

    private static string RequiredAttribute(XElement element, string name) =>
        element.Attribute(name)?.Value is { Length: > 0 } value
            ? value
            : throw new InvalidDataException($"Missing required '{name}' attribute.");

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ThemeIdPattern();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();
}
