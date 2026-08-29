using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using Harness.DataAccess.Inspection;

namespace Harness.DataAccess.Coverage;

internal sealed class CoberturaWorkspaceCoverageReader : IWorkspaceCoverageReader
{
    private const int MaximumReportBytes = 8 * 1024 * 1024;
    private const int MaximumFiles = 500;
    private const int MaximumLines = 100_000;

    public async ValueTask<WorkspaceCoverageReadResult> ReadAsync(
        string workspaceRoot,
        CoverageReportPath reportPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reportPath);
        if (!WorkspacePathPolicy.TryResolve(
                workspaceRoot, reportPath.Value, out string canonicalRoot,
                out string confinedReport, out string absoluteReport,
                out string? errorCode, out string? error))
            return Failure(reportPath, errorCode!, error!);
        FileInfo report = new(absoluteReport);
        if (!report.Exists || report.LinkTarget is not null ||
            !report.Extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
            return Failure(new(confinedReport), "coverage_report_unavailable",
                "The selected coverage report is missing, symbolic, or not XML.");
        if (report.Length > MaximumReportBytes)
            return Failure(new(confinedReport), "coverage_report_too_large",
                $"Coverage reports are limited to {MaximumReportBytes:N0} bytes.");

        byte[] content;
        try
        {
            content = await ReadBoundedAsync(absoluteReport, cancellationToken);
            if (content.Length > MaximumReportBytes)
                return Failure(new(confinedReport), "coverage_report_too_large",
                    $"Coverage reports are limited to {MaximumReportBytes:N0} bytes.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(new(confinedReport), "coverage_report_unavailable",
                "The selected coverage report could not be read safely.");
        }
        string hash = Convert.ToHexStringLower(SHA256.HashData(content));
        try
        {
            using MemoryStream stream = new(content, writable: false);
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                Async = true,
                DtdProcessing = DtdProcessing.Prohibit,
                MaxCharactersInDocument = MaximumReportBytes,
                XmlResolver = null,
            });
            XDocument document = await XDocument.LoadAsync(
                reader, LoadOptions.None, cancellationToken);
            XElement? root = document.Root;
            if (root?.Name.LocalName != "coverage")
                return Failure(new(confinedReport), "coverage_format_invalid",
                    "The selected XML document is not a Cobertura coverage report.");

            Dictionary<(string Path, int Line), long> lines = [];
            int unmapped = 0;
            int processedFiles = 0;
            bool truncated = false;
            foreach (IGrouping<string, XElement> file in document.Descendants()
                         .Where(element => element.Name.LocalName == "class")
                         .Where(element => element.Attribute("filename") is not null)
                         .GroupBy(element => element.Attribute("filename")!.Value,
                             StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (processedFiles++ >= MaximumFiles)
                {
                    truncated = true;
                    break;
                }
                if (!TryMapSource(canonicalRoot, file.Key, out string path))
                {
                    unmapped++;
                    continue;
                }
                foreach (XElement line in file.SelectMany(element => element.Descendants())
                             .Where(element => element.Name.LocalName == "line"))
                {
                    if (lines.Count >= MaximumLines)
                    {
                        truncated = true;
                        break;
                    }
                    if (!int.TryParse(line.Attribute("number")?.Value,
                            NumberStyles.None, CultureInfo.InvariantCulture, out int number) ||
                        number <= 0 || !long.TryParse(line.Attribute("hits")?.Value,
                            NumberStyles.None, CultureInfo.InvariantCulture, out long hits) ||
                        hits < 0)
                        continue;
                    (string Path, int Line) key = (path, number);
                    lines[key] = Math.Max(lines.GetValueOrDefault(key), hits);
                }
                if (lines.Count >= MaximumLines) break;
            }
            ImmutableArray<CoverageLineRecord> mapped = lines
                .OrderBy(item => item.Key.Path, StringComparer.Ordinal)
                .ThenBy(item => item.Key.Line)
                .Select(item => new CoverageLineRecord(
                    new(item.Key.Path), new(item.Key.Line), new(item.Value)))
                .ToImmutableArray();
            return new(
                new(confinedReport.Replace(Path.DirectorySeparatorChar, '/')),
                new(hash), CoverageReportFormat.Cobertura,
                Optional(root.Attribute("generator")?.Value, "Unknown Cobertura producer"),
                OptionalVersion(root.Attribute("version")?.Value, "unknown"),
                GeneratedAt(root.Attribute("timestamp")?.Value),
                mapped, unmapped, truncated, null, null);
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException
                                            or ArgumentException)
        {
            return Failure(new(confinedReport), "coverage_format_invalid",
                "The selected report is not valid bounded Cobertura XML.");
        }
    }

    private static bool TryMapSource(string root, string reportedPath, out string path)
    {
        path = string.Empty;
        string candidate = reportedPath.Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(candidate))
        {
            try
            {
                candidate = Path.GetRelativePath(root, Path.GetFullPath(candidate));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                return false;
            }
        }
        if (!WorkspacePathPolicy.TryResolve(root, candidate, out _, out string confined,
                out string absolute, out _, out _))
            return false;
        if (confined.Length is 0 or > 1_024) return false;
        FileInfo source = new(absolute);
        if (!source.Exists || source.LinkTarget is not null ||
            !(source.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
              source.Extension.Equals(".fs", StringComparison.OrdinalIgnoreCase) ||
              source.Extension.Equals(".vb", StringComparison.OrdinalIgnoreCase)))
            return false;
        path = confined.Replace(Path.DirectorySeparatorChar, '/');
        return true;
    }

    private static async ValueTask<byte[]> ReadBoundedAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream input = new(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using MemoryStream output = new((int)Math.Min(input.Length, MaximumReportBytes + 1L));
        byte[] buffer = new byte[64 * 1024];
        while (true)
        {
            int read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaximumReportBytes)
            {
                output.SetLength(MaximumReportBytes + 1L);
                break;
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return output.ToArray();
    }

    private static CoverageProducerName Optional(string? value, string fallback) =>
        new(Bounded(value, fallback));

    private static CoverageProducerVersion OptionalVersion(string? value, string fallback) =>
        new(Bounded(value, fallback));

    private static string Bounded(string? value, string fallback)
    {
        string normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        normalized = new(normalized.Where(character => !char.IsControl(character)).ToArray());
        if (string.IsNullOrWhiteSpace(normalized)) normalized = fallback;
        return normalized.Length > 128 ? normalized[..128] : normalized;
    }

    private static DateTimeOffset? GeneratedAt(string? timestamp) =>
        long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(seconds, 0, 253402300799))
            : null;

    private static WorkspaceCoverageReadResult Failure(
        CoverageReportPath path, string code, string error) => new(
        path, null, CoverageReportFormat.Cobertura, null, null, null, [], 0, false,
        code, error);
}
