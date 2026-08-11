using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Harness.DataAccess.Inspection;

namespace Harness.DataAccess.Mutations;

internal sealed partial class AtomicWorkspaceFileEditor : IWorkspaceFileEditor
{
    private const int MaximumBatchFiles = 100;
    private const int MaximumContentBytes = 1024 * 1024;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RootGates =
        new(StringComparer.Ordinal);
    private static readonly UTF8Encoding Utf8WithoutBom = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private readonly Func<int, Exception?>? beforeBatchCommit;

    public AtomicWorkspaceFileEditor()
    {
    }

    internal AtomicWorkspaceFileEditor(Func<int, Exception?> beforeBatchCommit)
    {
        this.beforeBatchCommit = beforeBatchCommit;
    }

    public async ValueTask<WorkspaceFileEditResult> ApplyAsync(
        string worktreeRoot,
        WorkspaceFileEdit edit,
        CancellationToken cancellationToken = default)
    {
        if (edit.ExpectedSha256 is not null && !Sha256Pattern().IsMatch(edit.ExpectedSha256))
        {
            return Failure(edit.Path, "invalid_hash", "The expected SHA-256 must be lowercase hexadecimal.");
        }

        int contentBytes = Utf8WithoutBom.GetByteCount(edit.Content);
        if (contentBytes > MaximumContentBytes)
        {
            return Failure(edit.Path, "content_too_large", "Edited content cannot exceed 1 MiB.");
        }

        if (!WorkspacePathPolicy.TryResolve(
                worktreeRoot,
                edit.Path,
                out _,
                out string confinedPath,
                out string targetPath,
                out string? errorCode,
                out string? error))
        {
            return Failure(confinedPath, errorCode!, error!);
        }

        string? parent = Path.GetDirectoryName(targetPath);
        if (parent is null || !Directory.Exists(parent))
        {
            return Failure(confinedPath, "parent_missing", "The destination directory does not exist.");
        }

        FileInfo target = new(targetPath);
        bool wasCreated = !target.Exists;
        if (target.Exists && target.Length > MaximumContentBytes)
        {
            return Failure(confinedPath, "existing_file_too_large", "The existing file exceeds 1 MiB.");
        }

        string? previousHash = target.Exists
            ? await HashAsync(target.FullName, cancellationToken)
            : null;
        if (!string.Equals(previousHash, edit.ExpectedSha256, StringComparison.Ordinal))
        {
            return Failure(
                confinedPath,
                "content_changed",
                "The file no longer matches the expected content hash.",
                previousHash);
        }

        string temporaryPath = Path.Combine(parent, $".harness-edit-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                edit.Content,
                Utf8WithoutBom,
                cancellationToken);
            if (target.Exists && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporaryPath, File.GetUnixFileMode(target.FullName));
            }

            string? latestHash = File.Exists(target.FullName)
                ? await HashAsync(target.FullName, cancellationToken)
                : null;
            if (!string.Equals(latestHash, edit.ExpectedSha256, StringComparison.Ordinal))
            {
                return Failure(
                    confinedPath,
                    "content_changed",
                    "The file changed while the edit was being prepared.",
                    latestHash);
            }

            File.Move(temporaryPath, target.FullName, overwrite: true);
            string newHash = Convert.ToHexStringLower(
                SHA256.HashData(Utf8WithoutBom.GetBytes(edit.Content)));
            return new(
                confinedPath,
                previousHash,
                newHash,
                contentBytes,
                wasCreated,
                ErrorCode: null,
                Error: null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(confinedPath, "write_failed", exception.Message, previousHash);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    public async ValueTask<WorkspaceFileBatchEditResult> ApplyBatchAsync(
        string worktreeRoot,
        WorkspaceFileBatchEdit batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (batch.Edits is null || batch.Edits.Count is 0 or > MaximumBatchFiles)
        {
            return BatchFailure("invalid_batch", $"A batch must contain 1-{MaximumBatchFiles} file edits.");
        }

        string root = Path.GetFullPath(worktreeRoot);
        SemaphoreSlim gate = RootGates.GetOrAdd(root, static _ => new(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await ApplyBatchCoreAsync(root, batch.Edits, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async ValueTask<WorkspaceFileBatchEditResult> ApplyBatchCoreAsync(
        string root,
        IReadOnlyList<WorkspaceFileEdit> edits,
        CancellationToken cancellationToken)
    {
        List<PreparedBatchEdit> prepared = new(edits.Count);
        HashSet<string> paths = new(StringComparer.Ordinal);
        try
        {
            foreach (WorkspaceFileEdit edit in edits)
            {
                if (edit.ExpectedSha256 is not null && !Sha256Pattern().IsMatch(edit.ExpectedSha256))
                {
                    return BatchFailure("invalid_hash", "Every expected SHA-256 must be lowercase hexadecimal.");
                }

                int contentBytes = Utf8WithoutBom.GetByteCount(edit.Content);
                if (contentBytes > MaximumContentBytes)
                {
                    return BatchFailure("content_too_large", "Edited content cannot exceed 1 MiB.");
                }

                if (!WorkspacePathPolicy.TryResolve(
                        root,
                        edit.Path,
                        out _,
                        out string confinedPath,
                        out string targetPath,
                        out string? errorCode,
                        out string? error))
                {
                    return BatchFailure(errorCode!, error!);
                }

                if (!paths.Add(confinedPath))
                {
                    return BatchFailure("duplicate_path", "A batch cannot edit the same normalized path twice.");
                }

                string? parent = Path.GetDirectoryName(targetPath);
                if (parent is null || !Directory.Exists(parent))
                {
                    return BatchFailure("parent_missing", "Every destination directory must already exist.");
                }

                FileInfo target = new(targetPath);
                if (target.Exists && target.Length > MaximumContentBytes)
                {
                    return BatchFailure("existing_file_too_large", "An existing file exceeds 1 MiB.");
                }

                string? previousHash = target.Exists
                    ? await HashAsync(target.FullName, cancellationToken)
                    : null;
                if (!string.Equals(previousHash, edit.ExpectedSha256, StringComparison.Ordinal))
                {
                    return BatchFailure("content_changed", $"{confinedPath} no longer matches its expected content hash.");
                }

                string temporaryPath = Path.Combine(parent, $".harness-batch-{Guid.NewGuid():N}.tmp");
                string? backupPath = target.Exists
                    ? Path.Combine(parent, $".harness-rollback-{Guid.NewGuid():N}.tmp")
                    : null;
                await File.WriteAllTextAsync(temporaryPath, edit.Content, Utf8WithoutBom, cancellationToken);
                if (target.Exists && !OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(temporaryPath, File.GetUnixFileMode(target.FullName));
                }

                prepared.Add(new(
                    confinedPath,
                    target.FullName,
                    temporaryPath,
                    backupPath,
                    previousHash,
                    Hash(edit.Content),
                    contentBytes,
                    !target.Exists));
            }

            foreach (PreparedBatchEdit item in prepared)
            {
                string? latestHash = File.Exists(item.TargetPath)
                    ? await HashAsync(item.TargetPath, cancellationToken)
                    : null;
                if (!string.Equals(latestHash, item.PreviousSha256, StringComparison.Ordinal))
                {
                    return BatchFailure("content_changed", $"{item.Path} changed while the batch was being prepared.");
                }
            }

            try
            {
                for (int index = 0; index < prepared.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Exception? injected = beforeBatchCommit?.Invoke(index);
                    if (injected is not null)
                    {
                        throw injected;
                    }

                    PreparedBatchEdit item = prepared[index];
                    if (item.BackupPath is not null)
                    {
                        File.Move(item.TargetPath, item.BackupPath);
                        item.BackupCreated = true;
                    }

                    File.Move(item.TemporaryPath, item.TargetPath);
                    item.Committed = true;
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                bool rolledBack = RollBack(prepared);
                return new(
                    prepared.Select(FailedResult).ToArray(),
                    rolledBack,
                    exception is OperationCanceledException,
                    exception is OperationCanceledException ? "cancelled" : "batch_write_failed",
                    exception.Message);
            }

            WorkspaceFileBatchEditResult result = new(
                prepared.Select(item => new WorkspaceFileEditResult(
                    item.Path,
                    item.PreviousSha256,
                    item.NewSha256,
                    item.BytesWritten,
                    item.WasCreated,
                    ErrorCode: null,
                    Error: null)).ToArray(),
                WasRolledBack: false,
                WasCancelled: false,
                ErrorCode: null,
                Error: null);
            foreach (PreparedBatchEdit item in prepared)
            {
                DeleteIfPresent(item.BackupPath);
                item.BackupCreated = false;
            }

            return result;
        }
        catch (OperationCanceledException exception)
        {
            return new([], WasRolledBack: false, WasCancelled: true, "cancelled", exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return BatchFailure("batch_prepare_failed", exception.Message);
        }
        finally
        {
            foreach (PreparedBatchEdit item in prepared)
            {
                DeleteIfPresent(item.TemporaryPath);
                if (!item.Committed)
                {
                    DeleteIfPresent(item.BackupPath);
                }
            }
        }
    }

    private static bool RollBack(IReadOnlyList<PreparedBatchEdit> prepared)
    {
        bool succeeded = true;
        for (int index = prepared.Count - 1; index >= 0; index--)
        {
            PreparedBatchEdit item = prepared[index];
            try
            {
                if (item.Committed)
                {
                    File.Delete(item.TargetPath);
                }

                if (item.BackupCreated && item.BackupPath is not null)
                {
                    File.Move(item.BackupPath, item.TargetPath);
                    item.BackupCreated = false;
                }

                item.Committed = false;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                succeeded = false;
            }
        }

        return succeeded;
    }

    private static WorkspaceFileEditResult FailedResult(PreparedBatchEdit item) => new(
        item.Path,
        item.PreviousSha256,
        NewSha256: null,
        BytesWritten: 0,
        WasCreated: false,
        "batch_not_applied",
        "The atomic batch was not applied.");

    private static WorkspaceFileBatchEditResult BatchFailure(string code, string error) =>
        new([], WasRolledBack: false, WasCancelled: false, code, error);

    private static void DeleteIfPresent(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static async ValueTask<string> HashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static string Hash(string content) => Convert.ToHexStringLower(
        SHA256.HashData(Utf8WithoutBom.GetBytes(content)));

    private static WorkspaceFileEditResult Failure(
        string path,
        string code,
        string error,
        string? previousHash = null) =>
        new(path, previousHash, null, 0, WasCreated: false, code, error);

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    private sealed class PreparedBatchEdit(
        string path,
        string targetPath,
        string temporaryPath,
        string? backupPath,
        string? previousSha256,
        string newSha256,
        int bytesWritten,
        bool wasCreated)
    {
        internal string Path { get; } = path;
        internal string TargetPath { get; } = targetPath;
        internal string TemporaryPath { get; } = temporaryPath;
        internal string? BackupPath { get; } = backupPath;
        internal string? PreviousSha256 { get; } = previousSha256;
        internal string NewSha256 { get; } = newSha256;
        internal int BytesWritten { get; } = bytesWritten;
        internal bool WasCreated { get; } = wasCreated;
        internal bool BackupCreated { get; set; }
        internal bool Committed { get; set; }
    }
}
