using Harness.DataAccess.Research;

namespace Harness.DataAccess.Tests.Research;

public sealed class AtomicSbomExporterTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"harness-sbom-{Guid.NewGuid():N}");

    public AtomicSbomExporterTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task Exports_only_to_explicit_absolute_json_destination_and_requires_overwrite_authority()
    {
        AtomicSbomExporter exporter = new();
        string path = Path.Combine(root, "bom.json");

        SbomExportOutcome created = await exporter.ExportAsync(path, new("{}\n", "hash"), false);
        SbomExportOutcome denied = await exporter.ExportAsync(path, new("{\"changed\":true}\n", "new"), false);
        SbomExportOutcome overwritten = await exporter.ExportAsync(path,
            new("{\"changed\":true}\n", "new"), true);
        SbomExportOutcome relative = await exporter.ExportAsync("bom.json", new("{}", "hash"), false);

        Assert.Null(created.ErrorCode);
        Assert.Equal("sbom_export_exists", denied.ErrorCode);
        Assert.Null(overwritten.ErrorCode);
        Assert.Equal("{\"changed\":true}\n", await File.ReadAllTextAsync(path));
        Assert.Equal("invalid_sbom_export_path", relative.ErrorCode);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
