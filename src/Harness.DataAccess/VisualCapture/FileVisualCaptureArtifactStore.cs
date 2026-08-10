using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Harness.DataAccess.Configuration;

namespace Harness.DataAccess.VisualCapture;

internal sealed partial class FileVisualCaptureArtifactStore(
    IApplicationPaths applicationPaths) : IVisualCaptureArtifactStore
{
    private const int MaximumManifestCount = 512;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async ValueTask<StoredVisualCapture> StoreAsync(
        StoredVisualCaptureWrite write,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        Validate(write);
        string directory = GoalDirectory(write.Capture.GoalId);
        Directory.CreateDirectory(directory);
        SetPrivateDirectory(directory);

        string id = write.Capture.Id.Value;
        string extension = write.Capture.MediaType == "image/png" ? ".png" : ".jpg";
        string artifactName = id + extension;
        string artifactPath = Path.Combine(directory, artifactName);
        string manifestPath = Path.Combine(directory, id + ".json");
        string nonce = Guid.NewGuid().ToString("N");
        string temporaryArtifact = Path.Combine(directory, $".{id}-{nonce}{extension}.tmp");
        string temporaryManifest = Path.Combine(directory, $".{id}-{nonce}.json.tmp");
        StoredVisualCapture stored = write.Capture with { ArtifactFileName = artifactName };
        try
        {
            await using (FileStream stream = new(
                             temporaryArtifact,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(write.Content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            SetPrivateFile(temporaryArtifact);
            await using (FileStream stream = new(
                             temporaryManifest,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream, stored, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            SetPrivateFile(temporaryManifest);
            File.Move(temporaryArtifact, artifactPath, overwrite: false);
            File.Move(temporaryManifest, manifestPath, overwrite: false);
            return stored;
        }
        finally
        {
            DeleteFile(temporaryArtifact);
            DeleteFile(temporaryManifest);
        }
    }

    public async ValueTask<IReadOnlyList<StoredVisualCapture>> ListAsync(
        string goalId,
        CancellationToken cancellationToken = default)
    {
        string directory = GoalDirectory(goalId);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        List<StoredVisualCapture> captures = [];
        foreach (string path in Directory.EnumerateFiles(directory, "*.json")
                     .Order(StringComparer.Ordinal)
                     .Take(MaximumManifestCount))
        {
            cancellationToken.ThrowIfCancellationRequested();
            StoredVisualCapture? capture = await ReadManifestAsync(path, cancellationToken);
            if (capture is not null && ValidManifest(capture, goalId, directory))
            {
                captures.Add(capture);
            }
        }
        return captures.OrderByDescending(item => item.CreatedAt).ToArray();
    }

    public async ValueTask<StoredVisualCaptureContent?> ReadAsync(
        string goalId,
        StoredVisualCaptureId captureId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(captureId);
        string directory = GoalDirectory(goalId);
        string manifestPath = Path.Combine(directory, captureId.Value + ".json");
        StoredVisualCapture? capture = await ReadManifestAsync(manifestPath, cancellationToken);
        if (capture is null || !ValidManifest(capture, goalId, directory))
        {
            return null;
        }

        string artifactPath = Path.Combine(directory, capture.ArtifactFileName);
        FileInfo info = new(artifactPath);
        if (!info.Exists || info.Length != capture.Bytes || info.Length > 16 * 1024 * 1024)
        {
            return null;
        }
        byte[] content = await File.ReadAllBytesAsync(artifactPath, cancellationToken);
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
        return sha256.Equals(capture.Sha256, StringComparison.Ordinal)
            ? new(capture, content)
            : null;
    }

    public ValueTask<bool> DeleteAsync(
        string goalId,
        StoredVisualCaptureId captureId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateId(captureId);
        string directory = GoalDirectory(goalId);
        string manifestPath = Path.Combine(directory, captureId.Value + ".json");
        string pngPath = Path.Combine(directory, captureId.Value + ".png");
        string jpegPath = Path.Combine(directory, captureId.Value + ".jpg");
        bool existed = File.Exists(manifestPath) || File.Exists(pngPath) || File.Exists(jpegPath);
        DeleteFile(manifestPath);
        DeleteFile(pngPath);
        DeleteFile(jpegPath);
        return ValueTask.FromResult(existed);
    }

    public async ValueTask<VisualCaptureCleanupResult> CleanupAsync(
        VisualCaptureRetentionPolicy policy,
        CancellationToken cancellationToken = default)
    {
        if (policy is null || policy.RetentionDays is < 1 or > 90 ||
            policy.MaximumCapturesPerGoal is < 1 or > 100)
        {
            throw new ArgumentException("The capture retention policy is invalid.");
        }

        string root = applicationPaths.Current.VisualCaptureDirectory;
        if (!Directory.Exists(root))
        {
            return new(0, 0, 0);
        }

        int removed = 0;
        int temporary = 0;
        int invalid = 0;
        DateTimeOffset cutoff = policy.Now.AddDays(-policy.RetentionDays);
        foreach (string path in Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories)
                     .Take(MaximumManifestCount * 2))
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeleteFile(path);
            temporary++;
        }

        foreach (string directory in Directory.EnumerateDirectories(root).Take(MaximumManifestCount))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string goalId = Path.GetFileName(directory);
            if (!ValidSegment(goalId))
            {
                continue;
            }
            List<StoredVisualCapture> valid = [];
            foreach (string manifest in Directory.EnumerateFiles(directory, "*.json")
                         .Take(MaximumManifestCount))
            {
                StoredVisualCapture? capture = await ReadManifestAsync(manifest, cancellationToken);
                if (capture is null || !ValidManifest(capture, goalId, directory))
                {
                    DeleteFile(manifest);
                    DeleteFile(Path.ChangeExtension(manifest, ".png"));
                    DeleteFile(Path.ChangeExtension(manifest, ".jpg"));
                    invalid++;
                }
                else
                {
                    valid.Add(capture);
                }
            }

            HashSet<string> retainedArtifacts = valid
                .Where(item => item.CreatedAt >= cutoff)
                .OrderByDescending(item => item.CreatedAt)
                .Take(policy.MaximumCapturesPerGoal)
                .Select(item => item.ArtifactFileName)
                .ToHashSet(StringComparer.Ordinal);
            foreach (StoredVisualCapture capture in valid)
            {
                if (!retainedArtifacts.Contains(capture.ArtifactFileName))
                {
                    await DeleteAsync(goalId, capture.Id, cancellationToken);
                    removed++;
                }
            }

            foreach (string artifact in Directory.EnumerateFiles(directory)
                         .Where(path => Path.GetExtension(path) is ".png" or ".jpg")
                         .Take(MaximumManifestCount))
            {
                if (!retainedArtifacts.Contains(Path.GetFileName(artifact)))
                {
                    DeleteFile(artifact);
                    invalid++;
                }
            }
        }
        return new(removed, temporary, invalid);
    }

    private string GoalDirectory(string goalId)
    {
        if (!ValidSegment(goalId))
        {
            throw new ArgumentException("The goal identifier is invalid.");
        }
        string root = applicationPaths.Current.VisualCaptureDirectory;
        Directory.CreateDirectory(root);
        SetPrivateDirectory(root);
        return Path.Combine(root, goalId);
    }

    private static async ValueTask<StoredVisualCapture?> ReadManifestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            FileInfo info = new(path);
            if (!info.Exists || info.Length is <= 0 or > 64 * 1024)
            {
                return null;
            }
            await using FileStream stream = new(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<StoredVisualCapture>(
                stream, JsonOptions, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or JsonException or
                                          UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Validate(StoredVisualCaptureWrite write)
    {
        ValidateId(write.Capture.Id);
        if (!ValidSegment(write.Capture.GoalId) || !ValidSegment(write.Capture.WorkspaceId) ||
            write.Content.IsEmpty || write.Content.Length != write.Capture.Bytes ||
            write.Capture.Bytes > 16 * 1024 * 1024 ||
            write.Capture.MediaType is not ("image/png" or "image/jpeg") ||
            write.Capture.ArtifactFileName.Length != 0 ||
            write.Capture.PixelWidth <= 0 || write.Capture.PixelHeight <= 0 ||
            !Convert.ToHexStringLower(SHA256.HashData(write.Content.Span))
                .Equals(write.Capture.Sha256, StringComparison.Ordinal))
        {
            throw new ArgumentException("The capture artifact is invalid.");
        }
    }

    private static bool ValidManifest(
        StoredVisualCapture capture,
        string goalId,
        string directory)
    {
        string extension = capture.MediaType == "image/png" ? ".png" :
            capture.MediaType == "image/jpeg" ? ".jpg" : string.Empty;
        return ValidId(capture.Id.Value) && capture.GoalId == goalId &&
            ValidSegment(capture.WorkspaceId) && capture.Bytes is > 0 and <= 16 * 1024 * 1024 &&
            capture.PixelWidth is > 0 and <= 16384 && capture.PixelHeight is > 0 and <= 16384 &&
            extension.Length > 0 && capture.ArtifactFileName == capture.Id.Value + extension &&
            capture.Sha256.Length == 64 && capture.Sha256.All(Uri.IsHexDigit) &&
            Path.GetFullPath(Path.Combine(directory, capture.ArtifactFileName))
                .StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal);
    }

    private static void ValidateId(StoredVisualCaptureId captureId)
    {
        if (captureId is null || !ValidId(captureId.Value))
        {
            throw new ArgumentException("The capture identifier is invalid.");
        }
    }

    private static bool ValidId(string value) => Guid.TryParseExact(value, "N", out _);
    private static bool ValidSegment(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && SegmentPattern().IsMatch(value);

    private static void SetPrivateDirectory(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void SetPrivateFile(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void DeleteFile(string path)
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

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SegmentPattern();
}
