using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Layouts;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Persistence;

internal sealed class SqliteApplicationRestore(
    IApplicationPaths applicationPaths,
    TimeProvider timeProvider) : IApplicationRestore
{
    private const string FormatV1 = "harness-backup-v1";
    private const string FormatV2 = "harness-backup-v2";
    private const long MaximumArchiveBytes = 4L * 1024 * 1024 * 1024;
    private const long MaximumDatabaseBytes = 4L * 1024 * 1024 * 1024;
    private const long MaximumLayoutBytes = FileWorkbenchLayoutStore.MaximumPayloadBytes + 4096L;
    private const long MaximumManifestBytes = 64 * 1024;
    private const int MaximumRollbackDirectories = 3;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public async ValueTask<ApplicationRestoreInspectionResult> InspectAsync(
        BackupArchivePath source,
        CancellationToken cancellationToken = default)
    {
        string? archivePath = ValidateSource(source);
        if (archivePath is null)
        {
            return InspectionFailure(ApplicationRestoreFailure.InvalidSource,
                "Select an existing absolute .zip backup archive outside Harness.NET restore storage.");
        }

        string inspectionDirectory = Path.Combine(
            RestoreRoot(), $".inspect-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(inspectionDirectory);
            return await ValidateAndExtractAsync(
                archivePath, inspectionDirectory, cancellationToken);
        }
        finally
        {
            DeleteDirectory(inspectionDirectory);
        }
    }

    public async ValueTask<ApplicationRestoreStageResult> StageAsync(
        BackupArchivePath source,
        BackupSha256 expectedArchiveSha256,
        CancellationToken cancellationToken = default)
    {
        string? archivePath = ValidateSource(source);
        if (archivePath is null)
        {
            return StageFailure(ApplicationRestoreFailure.InvalidSource,
                "Select an existing absolute .zip backup archive outside Harness.NET restore storage.");
        }

        string restoreRoot = RestoreRoot();
        string pendingDirectory = Path.Combine(restoreRoot, "pending");
        if (Directory.Exists(pendingDirectory))
        {
            return StageFailure(ApplicationRestoreFailure.PendingRestoreExists,
                "A verified restore is already pending. Restart Harness.NET to apply it.");
        }

        string stagingDirectory = Path.Combine(
            restoreRoot, $".staging-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            ApplicationRestoreInspectionResult validated = await ValidateAndExtractAsync(
                archivePath, stagingDirectory, cancellationToken);
            if (validated.Archive is null)
            {
                return new(null, RestartRequired: false, validated.Failure, validated.Error);
            }

            if (expectedArchiveSha256 is null ||
                !ValidSha(expectedArchiveSha256.Value) ||
                !validated.Archive.ArchiveSha256.Value.Equals(
                    expectedArchiveSha256.Value, StringComparison.OrdinalIgnoreCase))
            {
                return StageFailure(ApplicationRestoreFailure.IntegrityMismatch,
                    "The archive changed after inspection. Inspect it again before staging.");
            }

            PendingRestoreMarker marker = PendingRestoreMarker.From(
                validated.Archive, timeProvider.GetUtcNow());
            await WriteJsonAsync(
                Path.Combine(stagingDirectory, "restore.json"), marker, cancellationToken);
            SetPrivateMode(Path.Combine(stagingDirectory, "harness.db"));
            string stagedLayout = Path.Combine(stagingDirectory, "workbench-layout.json");
            if (File.Exists(stagedLayout))
            {
                SetPrivateMode(stagedLayout);
            }

            Directory.Move(stagingDirectory, pendingDirectory);
            return new(validated.Archive, RestartRequired: true, Failure: null, Error: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (Recoverable(exception))
        {
            return StageFailure(ApplicationRestoreFailure.StagingFailed, exception.Message);
        }
        finally
        {
            DeleteDirectory(stagingDirectory);
        }
    }

    public async ValueTask<ApplicationRestoreApplyResult> ApplyPendingAsync(
        CancellationToken cancellationToken = default)
    {
        string pendingDirectory = Path.Combine(RestoreRoot(), "pending");
        if (!Directory.Exists(pendingDirectory))
        {
            return new(
                HadPendingRestore: false,
                Applied: false,
                RestoredSchemaVersion: null,
                RollbackDirectory: null,
                Failure: null,
                Error: null);
        }

        string? rollbackDirectory = null;
        try
        {
            PendingRestoreMarker marker = await ReadMarkerAsync(
                Path.Combine(pendingDirectory, "restore.json"), cancellationToken);
            ApplicationRestoreInspectionResult staged = await ValidateStagedAsync(
                pendingDirectory, marker, cancellationToken);
            if (staged.Archive is null)
            {
                return new(true, false, null, null, staged.Failure, staged.Error);
            }

            rollbackDirectory = Path.Combine(
                RestoreRoot(), "rollbacks",
                $"{timeProvider.GetUtcNow():yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rollbackDirectory);
            ExistingState existing = await CaptureExistingAsync(
                rollbackDirectory, cancellationToken);
            try
            {
                await PublishAsync(pendingDirectory, marker, cancellationToken);
                await ValidatePublishedAsync(marker, cancellationToken);
            }
            catch
            {
                await RestoreExistingAsync(existing, cancellationToken);
                throw;
            }

            DeleteDirectory(pendingDirectory);
            PruneRollbacks(Path.GetDirectoryName(rollbackDirectory)!);
            return new(
                HadPendingRestore: true,
                Applied: true,
                staged.Archive.SchemaVersion,
                rollbackDirectory,
                Failure: null,
                Error: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (Recoverable(exception) || exception is JsonException)
        {
            return new(
                HadPendingRestore: true,
                Applied: false,
                RestoredSchemaVersion: null,
                rollbackDirectory,
                ApplicationRestoreFailure.ApplyFailed,
                exception.Message);
        }
    }

    private async ValueTask<ApplicationRestoreInspectionResult> ValidateAndExtractAsync(
        string archivePath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileInfo archiveFile = new(archivePath);
            if (archiveFile.Length is <= 0 or > MaximumArchiveBytes)
            {
                return InspectionFailure(ApplicationRestoreFailure.UnsupportedArchive,
                    "The backup archive is empty or exceeds the supported size.");
            }

            string initialArchiveSha = await HashAsync(archivePath, cancellationToken);

            BackupManifest manifest;
            await using (FileStream archiveStream = new(
                             archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                             81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (ZipArchive archive = new(archiveStream, ZipArchiveMode.Read, leaveOpen: false))
            {
                string[] names = archive.Entries.Select(entry => entry.FullName).ToArray();
                if (names.Distinct(StringComparer.Ordinal).Count() != names.Length ||
                    names.Any(name => name is not (
                        "manifest.json" or "harness.db" or "workbench-layout.json")))
                {
                    return InspectionFailure(ApplicationRestoreFailure.UnsupportedArchive,
                        "The archive contains duplicate, unknown, or unsafe entries.");
                }

                ZipArchiveEntry? manifestEntry = archive.GetEntry("manifest.json");
                ZipArchiveEntry? databaseEntry = archive.GetEntry("harness.db");
                if (manifestEntry is null || databaseEntry is null ||
                    manifestEntry.Length is <= 0 or > MaximumManifestBytes)
                {
                    return InspectionFailure(ApplicationRestoreFailure.UnsupportedArchive,
                        "The archive is missing its bounded manifest or database.");
                }

                await using (Stream manifestStream = manifestEntry.Open())
                {
                    manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(
                        manifestStream, JsonOptions, cancellationToken) ??
                        throw new InvalidDataException("The backup manifest is empty.");
                }

                string? manifestError = ValidateManifest(manifest, archive);
                if (manifestError is not null)
                {
                    return InspectionFailure(
                        ApplicationRestoreFailure.UnsupportedArchive, manifestError);
                }

                await ExtractAsync(
                    databaseEntry,
                    Path.Combine(destinationDirectory, "harness.db"),
                    manifest.DatabaseBytes,
                    cancellationToken);
                if (manifest.WorkbenchLayout is not null)
                {
                    await ExtractAsync(
                        archive.GetEntry(manifest.WorkbenchLayout.Entry)!,
                        Path.Combine(destinationDirectory, "workbench-layout.json"),
                        manifest.WorkbenchLayout.Bytes,
                        cancellationToken);
                }
            }

            string databasePath = Path.Combine(destinationDirectory, "harness.db");
            if (!await HashMatchesAsync(
                    databasePath, manifest.DatabaseSha256, cancellationToken))
            {
                return InspectionFailure(ApplicationRestoreFailure.IntegrityMismatch,
                    "The restored database does not match the manifest SHA-256.");
            }

            (int schemaVersion, string integrity) = await InspectDatabaseAsync(
                databasePath, cancellationToken);
            if (!integrity.Equals("ok", StringComparison.OrdinalIgnoreCase))
            {
                return InspectionFailure(ApplicationRestoreFailure.DatabaseInvalid,
                    $"SQLite integrity validation failed: {integrity}");
            }

            if (schemaVersion != manifest.SchemaVersion)
            {
                return InspectionFailure(ApplicationRestoreFailure.IntegrityMismatch,
                    "The database schema does not match the backup manifest.");
            }

            if (schemaVersion > SqliteDatabaseInitializer.CurrentSchemaVersion)
            {
                return InspectionFailure(ApplicationRestoreFailure.SchemaTooNew,
                    $"Backup schema {schemaVersion} is newer than supported schema " +
                    $"{SqliteDatabaseInitializer.CurrentSchemaVersion}.");
            }

            string? layoutSha = null;
            long? layoutBytes = null;
            if (manifest.WorkbenchLayout is not null)
            {
                string layoutPath = Path.Combine(destinationDirectory, "workbench-layout.json");
                if (!await HashMatchesAsync(
                        layoutPath, manifest.WorkbenchLayout.Sha256, cancellationToken))
                {
                    return InspectionFailure(ApplicationRestoreFailure.IntegrityMismatch,
                        "The restored workbench layout does not match the manifest SHA-256.");
                }

                WorkbenchLayoutStoreReadResult layout = await ValidateLayoutAsync(
                    destinationDirectory, cancellationToken);
                if (layout.Failure is not null)
                {
                    return InspectionFailure(ApplicationRestoreFailure.IntegrityMismatch,
                        $"Workbench layout validation failed: {layout.Error}");
                }

                layoutSha = manifest.WorkbenchLayout.Sha256;
                layoutBytes = manifest.WorkbenchLayout.Bytes;
            }

            string archiveSha = await HashAsync(archivePath, cancellationToken);
            if (!archiveSha.Equals(initialArchiveSha, StringComparison.OrdinalIgnoreCase))
            {
                return InspectionFailure(ApplicationRestoreFailure.IntegrityMismatch,
                    "The archive changed while it was being inspected.");
            }
            return new(new(
                new(archivePath),
                new(archiveSha),
                new(manifest.DatabaseSha256),
                new(manifest.DatabaseBytes),
                layoutSha is null ? null : new(layoutSha),
                layoutBytes is null ? null : new(layoutBytes.Value),
                new(schemaVersion),
                manifest.CreatedAt,
                manifest.Format == FormatV1
                    ? ApplicationBackupFormat.Version1
                    : ApplicationBackupFormat.Version2), Failure: null, Error: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (Recoverable(exception) || exception is JsonException)
        {
            return InspectionFailure(
                exception is SqliteException
                    ? ApplicationRestoreFailure.DatabaseInvalid
                    : ApplicationRestoreFailure.UnsupportedArchive,
                exception.Message);
        }
    }

    private async ValueTask<ApplicationRestoreInspectionResult> ValidateStagedAsync(
        string pendingDirectory,
        PendingRestoreMarker marker,
        CancellationToken cancellationToken)
    {
        if (marker.Format is not (FormatV1 or FormatV2) ||
            marker.SchemaVersion is < 1 or > SqliteDatabaseInitializer.CurrentSchemaVersion ||
            marker.DatabaseBytes is <= 0 or > MaximumDatabaseBytes ||
            !ValidSha(marker.DatabaseSha256) ||
            marker.WorkbenchLayoutBytes is < 0 or > MaximumLayoutBytes ||
            (marker.WorkbenchLayoutBytes is null) != (marker.WorkbenchLayoutSha256 is null))
        {
            return InspectionFailure(ApplicationRestoreFailure.IntegrityMismatch,
                "The pending restore marker is invalid.");
        }

        string databasePath = Path.Combine(pendingDirectory, "harness.db");
        if (!File.Exists(databasePath) || new FileInfo(databasePath).Length != marker.DatabaseBytes ||
            !await HashMatchesAsync(databasePath, marker.DatabaseSha256, cancellationToken))
        {
            return InspectionFailure(ApplicationRestoreFailure.IntegrityMismatch,
                "The staged database no longer matches the verified restore request.");
        }

        (int schema, string integrity) = await InspectDatabaseAsync(
            databasePath, cancellationToken);
        if (schema != marker.SchemaVersion ||
            !integrity.Equals("ok", StringComparison.OrdinalIgnoreCase))
        {
            return InspectionFailure(ApplicationRestoreFailure.DatabaseInvalid,
                "The staged SQLite database failed schema or integrity revalidation.");
        }

        if (marker.WorkbenchLayoutSha256 is not null)
        {
            string layoutPath = Path.Combine(pendingDirectory, "workbench-layout.json");
            if (!File.Exists(layoutPath) ||
                new FileInfo(layoutPath).Length != marker.WorkbenchLayoutBytes ||
                !await HashMatchesAsync(
                    layoutPath, marker.WorkbenchLayoutSha256, cancellationToken) ||
                (await ValidateLayoutAsync(pendingDirectory, cancellationToken)).Failure is not null)
            {
                return InspectionFailure(ApplicationRestoreFailure.IntegrityMismatch,
                    "The staged workbench layout failed revalidation.");
            }
        }

        return new(marker.ToArchive(), Failure: null, Error: null);
    }

    private string? ValidateSource(BackupArchivePath source)
    {
        string? value = source?.Value;
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value) ||
            !Path.GetExtension(value).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string path = Path.GetFullPath(value);
        string restoreRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(RestoreRoot()));
        return File.Exists(path) &&
               !path.StartsWith(restoreRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? path
            : null;
    }

    private static string? ValidateManifest(BackupManifest manifest, ZipArchive archive)
    {
        if (manifest.Format is not (FormatV1 or FormatV2) ||
            manifest.SchemaVersion < 1 ||
            manifest.DatabaseBytes is <= 0 or > MaximumDatabaseBytes ||
            !ValidSha(manifest.DatabaseSha256))
        {
            return "The backup manifest has an unsupported format or invalid database metadata.";
        }

        ZipArchiveEntry database = archive.GetEntry("harness.db")!;
        if (database.Length != manifest.DatabaseBytes)
        {
            return "The database entry size does not match the backup manifest.";
        }

        ZipArchiveEntry? layout = archive.GetEntry("workbench-layout.json");
        if (manifest.Format == FormatV1 && (manifest.WorkbenchLayout is not null || layout is not null))
        {
            return "Version-1 backup archives cannot contain workbench layout state.";
        }

        if ((manifest.WorkbenchLayout is null) != (layout is null))
        {
            return "The workbench layout entry does not match the backup manifest.";
        }

        if (manifest.WorkbenchLayout is { } layoutManifest &&
            (!layoutManifest.Entry.Equals("workbench-layout.json", StringComparison.Ordinal) ||
             layoutManifest.Bytes is <= 0 or > MaximumLayoutBytes ||
             layout!.Length != layoutManifest.Bytes ||
             !ValidSha(layoutManifest.Sha256)))
        {
            return "The workbench layout manifest is invalid.";
        }

        return null;
    }

    private async ValueTask<ExistingState> CaptureExistingAsync(
        string rollbackDirectory,
        CancellationToken cancellationToken)
    {
        string databasePath = applicationPaths.Current.DatabasePath;
        string rollbackDatabase = Path.Combine(rollbackDirectory, "harness.db");
        string rollbackWal = Path.Combine(rollbackDirectory, "harness.db-wal");
        string rollbackShm = Path.Combine(rollbackDirectory, "harness.db-shm");
        string rollbackLayout = Path.Combine(rollbackDirectory, "workbench-layout.json");
        await CopyIfExistsAsync(databasePath, rollbackDatabase, cancellationToken);
        await CopyIfExistsAsync(databasePath + "-wal", rollbackWal, cancellationToken);
        await CopyIfExistsAsync(databasePath + "-shm", rollbackShm, cancellationToken);
        await CopyIfExistsAsync(
            applicationPaths.Current.WorkbenchLayoutPath, rollbackLayout, cancellationToken);
        await WriteJsonAsync(Path.Combine(rollbackDirectory, "rollback.json"), new
        {
            CreatedAt = timeProvider.GetUtcNow(),
            HadDatabase = File.Exists(rollbackDatabase),
            HadWal = File.Exists(rollbackWal),
            HadShm = File.Exists(rollbackShm),
            HadLayout = File.Exists(rollbackLayout),
        }, cancellationToken);
        return new(
            rollbackDirectory,
            File.Exists(rollbackDatabase),
            File.Exists(rollbackWal),
            File.Exists(rollbackShm),
            File.Exists(rollbackLayout));
    }

    private async ValueTask PublishAsync(
        string pendingDirectory,
        PendingRestoreMarker marker,
        CancellationToken cancellationToken)
    {
        string databasePath = applicationPaths.Current.DatabasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await PublishFileAsync(
            Path.Combine(pendingDirectory, "harness.db"), databasePath, cancellationToken);
        File.Delete(databasePath + "-wal");
        File.Delete(databasePath + "-shm");
        string layoutPath = applicationPaths.Current.WorkbenchLayoutPath;
        if (marker.WorkbenchLayoutSha256 is null)
        {
            File.Delete(layoutPath);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(layoutPath)!);
            await PublishFileAsync(
                Path.Combine(pendingDirectory, "workbench-layout.json"),
                layoutPath,
                cancellationToken);
        }
    }

    private async ValueTask ValidatePublishedAsync(
        PendingRestoreMarker marker,
        CancellationToken cancellationToken)
    {
        string databasePath = applicationPaths.Current.DatabasePath;
        if (!await HashMatchesAsync(databasePath, marker.DatabaseSha256, cancellationToken))
        {
            throw new InvalidDataException("Published database hash validation failed.");
        }

        (int schema, string integrity) = await InspectDatabaseAsync(
            databasePath, cancellationToken);
        if (schema != marker.SchemaVersion ||
            !integrity.Equals("ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Published database integrity validation failed.");
        }

        if (marker.WorkbenchLayoutSha256 is not null &&
            !await HashMatchesAsync(
                applicationPaths.Current.WorkbenchLayoutPath,
                marker.WorkbenchLayoutSha256,
                cancellationToken))
        {
            throw new InvalidDataException("Published workbench layout validation failed.");
        }
    }

    private async ValueTask RestoreExistingAsync(
        ExistingState existing,
        CancellationToken cancellationToken)
    {
        await RestoreFileAsync(
            Path.Combine(existing.Directory, "harness.db"),
            applicationPaths.Current.DatabasePath,
            existing.HadDatabase,
            cancellationToken);
        await RestoreFileAsync(
            Path.Combine(existing.Directory, "harness.db-wal"),
            applicationPaths.Current.DatabasePath + "-wal",
            existing.HadWal,
            cancellationToken);
        await RestoreFileAsync(
            Path.Combine(existing.Directory, "harness.db-shm"),
            applicationPaths.Current.DatabasePath + "-shm",
            existing.HadShm,
            cancellationToken);
        await RestoreFileAsync(
            Path.Combine(existing.Directory, "workbench-layout.json"),
            applicationPaths.Current.WorkbenchLayoutPath,
            existing.HadLayout,
            cancellationToken);
    }

    private static async ValueTask RestoreFileAsync(
        string source,
        string destination,
        bool existed,
        CancellationToken cancellationToken)
    {
        if (!existed)
        {
            File.Delete(destination);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await PublishFileAsync(source, destination, cancellationToken);
    }

    private static async ValueTask PublishFileAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        string temporary = destination + $".restore-{Guid.NewGuid():N}.tmp";
        try
        {
            await CopyAsync(source, temporary, cancellationToken);
            SetPrivateMode(temporary);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static async ValueTask ExtractAsync(
        ZipArchiveEntry entry,
        string destination,
        long expectedBytes,
        CancellationToken cancellationToken)
    {
        if (entry.Length != expectedBytes)
        {
            throw new InvalidDataException("Archive entry size changed during validation.");
        }

        await using Stream source = entry.Open();
        await using FileStream target = new(
            destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
        byte[] buffer = new byte[81920];
        long written = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            written = checked(written + read);
            if (written > expectedBytes)
            {
                throw new InvalidDataException("Archive entry expanded beyond its manifest size.");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (written != expectedBytes)
        {
            throw new InvalidDataException("Archive entry ended before its manifest size.");
        }

        await target.FlushAsync(cancellationToken);
        target.Flush(flushToDisk: true);
    }

    private async ValueTask<WorkbenchLayoutStoreReadResult> ValidateLayoutAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        ApplicationPaths current = applicationPaths.Current;
        ApplicationPaths temporaryPaths = new(
            current.ConfigDirectory,
            current.DataDirectory,
            directory,
            current.CacheDirectory,
            current.DatabasePath,
            current.LogDirectory,
            current.WorktreeDirectory);
        return await new FileWorkbenchLayoutStore(new FixedApplicationPaths(temporaryPaths))
            .ReadAsync(cancellationToken);
    }

    private static async ValueTask<(int Schema, string Integrity)> InspectDatabaseAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true,
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand schema = connection.CreateCommand();
        schema.CommandText =
            "SELECT value FROM application_metadata WHERE key = 'schema_version';";
        object? schemaValue = await schema.ExecuteScalarAsync(cancellationToken);
        if (schemaValue is not string text ||
            !int.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out int version))
        {
            throw new InvalidDataException("The database has no valid Harness.NET schema version.");
        }

        await using SqliteCommand integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check;";
        string result = (string)(await integrity.ExecuteScalarAsync(cancellationToken) ?? string.Empty);
        return (version, result);
    }

    private async ValueTask<PendingRestoreMarker> ReadMarkerAsync(
        string path,
        CancellationToken cancellationToken)
    {
        FileInfo file = new(path);
        if (!file.Exists || file.Length is <= 0 or > MaximumManifestBytes)
        {
            throw new InvalidDataException("The pending restore marker is missing or oversized.");
        }

        await using FileStream stream = new(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<PendingRestoreMarker>(
            stream, JsonOptions, cancellationToken) ??
            throw new InvalidDataException("The pending restore marker is empty.");
    }

    private static async ValueTask WriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
        SetPrivateMode(path);
    }

    private static async ValueTask CopyIfExistsAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        if (File.Exists(source))
        {
            await CopyAsync(source, destination, cancellationToken);
            SetPrivateMode(destination);
        }
    }

    private static async ValueTask CopyAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        await using FileStream input = new(
            source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using FileStream output = new(
            destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
        output.Flush(flushToDisk: true);
    }

    private static async ValueTask<bool> HashMatchesAsync(
        string path,
        string expected,
        CancellationToken cancellationToken)
    {
        if (!ValidSha(expected))
        {
            return false;
        }

        byte[] expectedBytes = Convert.FromHexString(expected);
        await using FileStream stream = new(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] actual = await SHA256.HashDataAsync(stream, cancellationToken);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actual);
    }

    private static async ValueTask<string> HashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private string RestoreRoot() => Path.Combine(
        applicationPaths.Current.DataDirectory, "restores");

    private static bool ValidSha(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool Recoverable(Exception exception) => exception is
        IOException or UnauthorizedAccessException or InvalidDataException or
        NotSupportedException or SqliteException or CryptographicException;

    private static void SetPrivateMode(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void PruneRollbacks(string root)
    {
        DirectoryInfo[] rollbacks = new DirectoryInfo(root).GetDirectories()
            .OrderByDescending(directory => directory.Name, StringComparer.Ordinal)
            .ToArray();
        foreach (DirectoryInfo old in rollbacks.Skip(MaximumRollbackDirectories))
        {
            DeleteDirectory(old.FullName);
        }
    }

    private static ApplicationRestoreInspectionResult InspectionFailure(
        ApplicationRestoreFailure failure,
        string error) => new(null, failure, error);

    private static ApplicationRestoreStageResult StageFailure(
        ApplicationRestoreFailure failure,
        string error) => new(null, RestartRequired: false, failure, error);

    private sealed record BackupManifest(
        string Format,
        int SchemaVersion,
        DateTimeOffset CreatedAt,
        long DatabaseBytes,
        string DatabaseSha256,
        BackupFileManifest? WorkbenchLayout);

    private sealed record BackupFileManifest(
        string Entry,
        long Bytes,
        string Sha256);

    private sealed record PendingRestoreMarker(
        string Format,
        int SchemaVersion,
        DateTimeOffset CreatedAt,
        DateTimeOffset StagedAt,
        string ArchivePath,
        string ArchiveSha256,
        long DatabaseBytes,
        string DatabaseSha256,
        long? WorkbenchLayoutBytes,
        string? WorkbenchLayoutSha256)
    {
        internal static PendingRestoreMarker From(
            ApplicationRestoreArchive archive,
            DateTimeOffset stagedAt) => new(
            archive.Format is ApplicationBackupFormat.Version1 ? FormatV1 : FormatV2,
            archive.SchemaVersion.Value,
            archive.CreatedAt,
            stagedAt,
            archive.Archive.Value,
            archive.ArchiveSha256.Value,
            archive.DatabaseBytes.Value,
            archive.DatabaseSha256.Value,
            archive.WorkbenchLayoutBytes?.Value,
            archive.WorkbenchLayoutSha256?.Value);

        internal ApplicationRestoreArchive ToArchive() => new(
            new(ArchivePath),
            new(ArchiveSha256),
            new(DatabaseSha256),
            new(DatabaseBytes),
            WorkbenchLayoutSha256 is null ? null : new(WorkbenchLayoutSha256),
            WorkbenchLayoutBytes is null ? null : new(WorkbenchLayoutBytes.Value),
            new(SchemaVersion),
            CreatedAt,
            Format == FormatV1
                ? ApplicationBackupFormat.Version1
                : ApplicationBackupFormat.Version2);
    }

    private sealed record ExistingState(
        string Directory,
        bool HadDatabase,
        bool HadWal,
        bool HadShm,
        bool HadLayout);

    private sealed class FixedApplicationPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
