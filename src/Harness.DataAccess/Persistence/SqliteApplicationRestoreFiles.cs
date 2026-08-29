using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Layouts;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Persistence;

internal sealed partial class SqliteApplicationRestore
{
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

}
