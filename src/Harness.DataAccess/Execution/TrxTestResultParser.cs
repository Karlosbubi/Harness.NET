using System.Collections.Immutable;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Harness.DataAccess.Execution;

internal static class TrxTestResultParser
{
    private const int MaximumCases = 2_000;
    private const long MaximumFileBytes = 4 * 1024 * 1024;

    internal static TrxTestResultParse ParseDirectory(string directory)
    {
        List<DotNetTestCaseResult> cases = [];
        bool truncated = false;
        foreach (string path in Directory.EnumerateFiles(directory, "*.trx")
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            FileInfo file = new(path);
            if (file.LinkTarget is not null || file.Length > MaximumFileBytes)
            {
                truncated = true;
                continue;
            }
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                MaxCharactersInDocument = MaximumFileBytes,
                XmlResolver = null,
            });
            XDocument document = XDocument.Load(reader, LoadOptions.None);
            Dictionary<string, string> names = document.Descendants()
                .Where(element => element.Name.LocalName == "UnitTest")
                .Select(element => new
                {
                    Id = element.Attribute("id")?.Value,
                    Method = element.Descendants().FirstOrDefault(item =>
                        item.Name.LocalName == "TestMethod"),
                })
                .Where(item => item.Id is not null && item.Method is not null)
                .ToDictionary(
                    item => item.Id!,
                    item => QualifiedName(item.Method!),
                    StringComparer.Ordinal);
            foreach (XElement result in document.Descendants().Where(element =>
                         element.Name.LocalName == "UnitTestResult"))
            {
                if (cases.Count >= MaximumCases)
                {
                    truncated = true;
                    break;
                }
                string displayName = Bounded(result.Attribute("testName")?.Value, 1_024);
                string testId = result.Attribute("testId")?.Value ?? string.Empty;
                string fullyQualifiedName = names.GetValueOrDefault(testId) ?? displayName;
                if (string.IsNullOrWhiteSpace(fullyQualifiedName)) continue;
                cases.Add(new(
                    new(Bounded(fullyQualifiedName, 512)),
                    new(string.IsNullOrWhiteSpace(displayName)
                        ? Bounded(fullyQualifiedName, 1_024)
                        : displayName),
                    Outcome(result.Attribute("outcome")?.Value),
                    Duration(result.Attribute("duration")?.Value)));
            }
            if (cases.Count >= MaximumCases) break;
        }
        return new(cases.ToImmutableArray(), truncated);
    }

    private static string QualifiedName(XElement method)
    {
        string type = method.Attribute("className")?.Value ?? string.Empty;
        string name = method.Attribute("name")?.Value ?? string.Empty;
        return string.IsNullOrWhiteSpace(type) ? name : $"{type}.{name}";
    }

    private static DotNetTestOutcome Outcome(string? value) => value switch
    {
        "Passed" => DotNetTestOutcome.Passed,
        "Failed" or "Timeout" or "Aborted" => DotNetTestOutcome.Failed,
        "NotExecuted" or "Inconclusive" => DotNetTestOutcome.Skipped,
        _ => DotNetTestOutcome.Other,
    };

    private static long Duration(string? value)
    {
        if (value is null || !TimeSpan.TryParse(value, CultureInfo.InvariantCulture,
                out TimeSpan duration) || duration < TimeSpan.Zero)
            return 0;
        return Math.Min((long)duration.TotalMilliseconds, int.MaxValue);
    }

    private static string Bounded(string? value, int maximum)
    {
        string trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length > maximum ? trimmed[..maximum] : trimmed;
    }
}

internal sealed record TrxTestResultParse(
    ImmutableArray<DotNetTestCaseResult> Cases,
    bool IsTruncated);
