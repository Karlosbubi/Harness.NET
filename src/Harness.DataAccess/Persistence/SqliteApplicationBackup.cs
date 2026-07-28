using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Persistence;

internal sealed class SqliteApplicationBackup(
    IApplicationPaths applicationPaths,
    TimeProvider timeProvider) : IApplicationBackup
{
    private const string FormatVersion = "harness-backup-v1";

    public async ValueTask<ApplicationBackupResult> CreateAsync(
        ApplicationBackupRequest request,
        CancellationToken cancellationToken = default)
    {
        string? destination = Validate(request);
        if (destination is null)
        {
            return Failure(ApplicationBackupFailure.InvalidDestination,
                "An absolute, new .zip path in an existing directory is required.");
        }

        string databasePath = applicationPaths.Current.DatabasePath;
        if (!File.Exists(databasePath))
        {
            return Failure(ApplicationBackupFailure.DatabaseMissing,
                "The Harness.NET database does not exist.");
        }

        string directory = Path.GetDirectoryName(destination)!;
        string nonce = Guid.NewGuid().ToString("N");
        string snapshotPath = Path.Combine(directory, $".harness-backup-{nonce}.db");
        string archivePath = Path.Combine(directory, $".harness-backup-{nonce}.zip");
        DateTimeOffset createdAt = timeProvider.GetUtcNow();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using (SqliteConnection source = Connection(
                             databasePath, SqliteOpenMode.ReadOnly))
            await using (SqliteConnection target = Connection(
                             snapshotPath, SqliteOpenMode.ReadWriteCreate))
            {
                await source.OpenAsync(cancellationToken);
                await target.OpenAsync(cancellationToken);
                source.BackupDatabase(target);
            }

            cancellationToken.ThrowIfCancellationRequested();
            (int schemaVersion, string integrity) = await InspectAsync(
                snapshotPath, cancellationToken);
            if (!integrity.Equals("ok", StringComparison.OrdinalIgnoreCase))
            {
                return Failure(ApplicationBackupFailure.IntegrityCheckFailed,
                    $"SQLite integrity validation failed: {integrity}");
            }

            long databaseBytes = new FileInfo(snapshotPath).Length;
            string databaseSha256 = await HashAsync(snapshotPath, cancellationToken);
            BackupManifest manifest = new(
                FormatVersion,
                schemaVersion,
                createdAt,
                databaseBytes,
                databaseSha256);
            using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(snapshotPath, "harness.db",
                    CompressionLevel.Optimal);
                ZipArchiveEntry manifestEntry = archive.CreateEntry(
                    "manifest.json", CompressionLevel.Optimal);
                await using Stream stream = manifestEntry.Open();
                await JsonSerializer.SerializeAsync(
                    stream, manifest, cancellationToken: cancellationToken);
            }

            string archiveSha256 = await HashAsync(archivePath, cancellationToken);
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(archivePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(archivePath, destination, overwrite: false);

            return new(
                new(destination),
                new(archiveSha256),
                new(databaseSha256),
                new(databaseBytes),
                new(schemaVersion),
                createdAt,
                Failure: null,
                Error: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or SqliteException or
                                          UnauthorizedAccessException or InvalidDataException)
        {
            return Failure(ApplicationBackupFailure.ArchiveCreationFailed,
                exception.Message);
        }
        finally
        {
            DeleteTemporary(snapshotPath);
            DeleteTemporary(archivePath);
        }
    }

    private static string? Validate(ApplicationBackupRequest request)
    {
        string? value = request?.Destination?.Value;
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value) ||
            !Path.GetExtension(value).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string path = Path.GetFullPath(value);
        string? directory = Path.GetDirectoryName(path);
        return directory is not null && Directory.Exists(directory) && !File.Exists(path)
            ? path
            : null;
    }

    private static async ValueTask<(int SchemaVersion, string Integrity)> InspectAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = Connection(path, SqliteOpenMode.ReadOnly);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand schema = connection.CreateCommand();
        schema.CommandText =
            "SELECT value FROM application_metadata WHERE key = 'schema_version';";
        int schemaVersion = int.Parse(
            (string)(await schema.ExecuteScalarAsync(cancellationToken))!,
            System.Globalization.CultureInfo.InvariantCulture);
        await using SqliteCommand integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check;";
        string result = (string)(await integrity.ExecuteScalarAsync(cancellationToken))!;
        return (schemaVersion, result);
    }

    private static async ValueTask<string> HashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static SqliteConnection Connection(string path, SqliteOpenMode mode) => new(
        new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            ForeignKeys = true,
        }.ToString());

    private static void DeleteTemporary(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static ApplicationBackupResult Failure(
        ApplicationBackupFailure failure,
        string error) => new(
        Archive: null,
        ArchiveSha256: null,
        DatabaseSha256: null,
        DatabaseBytes: null,
        SchemaVersion: null,
        CreatedAt: null,
        failure,
        error);

    private sealed record BackupManifest(
        string Format,
        int SchemaVersion,
        DateTimeOffset CreatedAt,
        long DatabaseBytes,
        string DatabaseSha256);
}
