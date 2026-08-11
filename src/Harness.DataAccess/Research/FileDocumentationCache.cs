using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Harness.DataAccess.Configuration;

namespace Harness.DataAccess.Research;

internal sealed class FileDocumentationCache(IApplicationPaths applicationPaths)
    : IDocumentationCache
{
    private const int MaximumEntries = 10_000;
    private const long MaximumEntryBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? lastFailure;

    public async ValueTask<DocumentationCacheEntry?> GetAsync(
        DocumentationCacheKey key,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            string path = EntryPath(key);
            FileInfo file = new(path);
            if (!file.Exists)
            {
                return null;
            }
            if (file.Length > MaximumEntryBytes)
            {
                lastFailure = $"Cache entry {file.Name} exceeds the 2 MiB limit.";
                return null;
            }
            await using FileStream stream = file.OpenRead();
            DocumentationCacheEntry? entry = await JsonSerializer.DeserializeAsync<DocumentationCacheEntry>(
                stream, JsonOptions, cancellationToken);
            if (entry is null || !entry.Key.Equals(key))
            {
                lastFailure = $"Cache entry {file.Name} has an invalid identity.";
                return null;
            }
            return entry;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            JsonException or NotSupportedException)
        {
            lastFailure = exception.Message;
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask PutAsync(
        DocumentationCacheEntry entry,
        CancellationToken cancellationToken = default)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions);
        if (json.Length > MaximumEntryBytes)
        {
            throw new InvalidDataException("The documentation cache entry exceeds 2 MiB.");
        }
        await gate.WaitAsync(cancellationToken);
        try
        {
            string directory = DirectoryPath();
            Directory.CreateDirectory(directory);
            string target = EntryPath(entry.Key);
            string temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllBytesAsync(temporary, json, cancellationToken);
                File.Move(temporary, target, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<DocumentationCacheStatus> CleanupAsync(
        DateTimeOffset retainAfter,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            string directory = DirectoryPath();
            if (Directory.Exists(directory))
            {
                foreach (FileInfo file in new DirectoryInfo(directory).EnumerateFiles("*.json")
                             .OrderByDescending(file => file.LastWriteTimeUtc)
                             .Skip(MaximumEntries)
                             .Concat(new DirectoryInfo(directory).EnumerateFiles("*.json")
                                 .Where(file => file.LastWriteTimeUtc < retainAfter.UtcDateTime))
                             .DistinctBy(file => file.FullName))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    file.Delete();
                }
            }
            return StatusUnsafe();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            lastFailure = exception.Message;
            return StatusUnsafe();
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<DocumentationCacheStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            return StatusUnsafe();
        }
        finally
        {
            gate.Release();
        }
    }

    private DocumentationCacheStatus StatusUnsafe()
    {
        string directory = DirectoryPath();
        if (!Directory.Exists(directory))
        {
            return new(0, 0, null, null, lastFailure);
        }
        FileInfo[] files = new DirectoryInfo(directory).EnumerateFiles("*.json")
            .Take(MaximumEntries + 1).ToArray();
        return new(
            files.Length,
            files.Sum(file => file.Length),
            files.Length == 0 ? null : files.Min(file => new DateTimeOffset(file.LastWriteTimeUtc)),
            files.Length == 0 ? null : files.Max(file => new DateTimeOffset(file.LastWriteTimeUtc)),
            lastFailure);
    }

    private string EntryPath(DocumentationCacheKey key)
    {
        string canonical = string.Join('\n',
            key.Source.Value.Trim().ToLowerInvariant(),
            key.Library.Value.Trim().ToLowerInvariant(),
            key.Version?.Value.Trim().ToLowerInvariant() ?? string.Empty,
            key.Query.Value.Trim(),
            key.AdapterSchemaVersion,
            key.DisclosureClass.ToString());
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
        return Path.Combine(DirectoryPath(), digest + ".json");
    }

    private string DirectoryPath() => Path.Combine(
        applicationPaths.Current.CacheDirectory, "documentation");
}
