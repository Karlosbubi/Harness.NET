using System.Text;

namespace Harness.DataAccess.Research;

internal sealed class AtomicSbomExporter : ISbomExporter
{
    public async ValueTask<SbomExportOutcome> ExportAsync(
        string path,
        SbomExportContent content,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        string target;
        try
        {
            if (!Path.IsPathFullyQualified(path) ||
                !Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                return Failure(path, "invalid_sbom_export_path",
                    "Choose an absolute .json destination for the SBOM.");
            }
            target = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return Failure(path, "invalid_sbom_export_path", exception.Message);
        }
        if (File.Exists(target) && !overwrite)
        {
            return Failure(target, "sbom_export_exists",
                "The destination exists and overwrite was not authorized.");
        }
        string? directory = Path.GetDirectoryName(target);
        if (directory is null || !Directory.Exists(directory))
        {
            return Failure(target, "sbom_export_directory_missing",
                "The destination directory does not exist.");
        }
        string temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(content.Json);
            await using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporary, target, overwrite);
            return new(target, content.Sha256, bytes.LongLength, null, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(target, "sbom_export_failed", exception.Message);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static SbomExportOutcome Failure(string path, string code, string error) =>
        new(path, null, 0, code, error);
}
