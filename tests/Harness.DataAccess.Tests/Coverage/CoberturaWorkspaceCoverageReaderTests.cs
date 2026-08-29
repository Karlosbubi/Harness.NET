using Harness.DataAccess.Coverage;

namespace Harness.DataAccess.Tests.Coverage;

public sealed class CoberturaWorkspaceCoverageReaderTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "harness-coverage-reader-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Maps_only_existing_workspace_sources_and_preserves_bounded_provenance()
    {
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "artifacts"));
        string source = Path.Combine(root, "src", "Example.cs");
        await File.WriteAllTextAsync(source, "class Example { }\n");
        string absoluteSource = source.Replace("&", "&amp;", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(root, "artifacts", "coverage.xml"), $$"""
            <?xml version="1.0"?>
            <coverage generator=" coverlet.console " version="6.0.4" timestamp="1788000000">
              <packages><package><classes>
                <class filename="src/Example.cs"><lines>
                  <line number="3" hits="0" />
                  <line number="5" hits="2" />
                </lines></class>
                <class filename="{{absoluteSource}}"><lines>
                  <line number="5" hits="7" />
                </lines></class>
                <class filename="../outside.cs"><lines>
                  <line number="1" hits="1" />
                </lines></class>
              </classes></package></packages>
            </coverage>
            """);

        WorkspaceCoverageReadResult result = await new CoberturaWorkspaceCoverageReader()
            .ReadAsync(root, new("artifacts/coverage.xml"));

        Assert.Null(result.Error);
        Assert.Equal("artifacts/coverage.xml", result.ReportPath.Value);
        Assert.Equal(64, result.ReportHash?.Value.Length);
        Assert.Equal(CoverageReportFormat.Cobertura, result.Format);
        Assert.Equal("coverlet.console", result.Producer?.Value);
        Assert.Equal("6.0.4", result.ProducerVersion?.Value);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_788_000_000), result.GeneratedAt);
        Assert.Equal(1, result.UnmappedFileCount);
        Assert.False(result.IsTruncated);
        Assert.Equal([3, 5], result.Lines.Select(line => line.Line.Value));
        Assert.All(result.Lines, line => Assert.Equal("src/Example.cs", line.Path.Value));
        Assert.Equal(7, result.Lines[1].Hits.Value);
    }

    [Fact]
    public async Task Rejects_outside_reports_and_DTD_documents()
    {
        Directory.CreateDirectory(root);
        string outside = OutsideReport();
        await File.WriteAllTextAsync(outside, "<coverage />");
        await File.WriteAllTextAsync(Path.Combine(root, "coverage.xml"), """
            <!DOCTYPE coverage [<!ENTITY content SYSTEM "file:///etc/passwd">]>
            <coverage generator="unsafe"><sources><source>&content;</source></sources></coverage>
            """);

        CoberturaWorkspaceCoverageReader reader = new();
        WorkspaceCoverageReadResult escaped = await reader.ReadAsync(
            root, new($"../{Path.GetFileName(outside)}"));
        WorkspaceCoverageReadResult dtd = await reader.ReadAsync(root, new("coverage.xml"));

        Assert.Equal("outside_workspace", escaped.ErrorCode);
        Assert.Equal("coverage_format_invalid", dtd.ErrorCode);
    }

    [Fact]
    public async Task Rejects_reports_larger_than_the_bounded_import_limit()
    {
        Directory.CreateDirectory(root);
        await File.WriteAllBytesAsync(
            Path.Combine(root, "large.xml"), new byte[8 * 1024 * 1024 + 1]);

        WorkspaceCoverageReadResult result = await new CoberturaWorkspaceCoverageReader()
            .ReadAsync(root, new("large.xml"));

        Assert.Equal("coverage_report_too_large", result.ErrorCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        string outside = OutsideReport();
        if (File.Exists(outside)) File.Delete(outside);
    }

    private string OutsideReport() => Path.Combine(
        Path.GetDirectoryName(root)!, $"outside-{Path.GetFileName(root)}.xml");
}
