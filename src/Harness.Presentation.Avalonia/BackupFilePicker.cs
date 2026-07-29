using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Harness.Presentation.Avalonia;

internal sealed record BackupFilePath(string Value);

internal sealed record BackupFilePickerResult(BackupFilePath? File, string? Error);

internal interface IBackupFilePicker
{
    ValueTask<BackupFilePickerResult> PickAsync(
        TopLevel owner,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Chooses the application-state archive destination through the platform save dialog.
/// Manual entry stays available because the destination must remain typeable where no
/// desktop picker exists.
/// </summary>
internal sealed class AvaloniaBackupFilePicker : IBackupFilePicker
{
    public async ValueTask<BackupFilePickerResult> PickAsync(
        TopLevel owner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        cancellationToken.ThrowIfCancellationRequested();
        IStorageProvider storage = owner.StorageProvider;
        if (!storage.CanSave)
        {
            return new(null, "This desktop does not provide a save dialog. Enter the archive path instead.");
        }

        try
        {
            IStorageFile? file = await storage.SaveFilePickerAsync(new()
            {
                Title = "Create an application-state backup",
                SuggestedFileName = $"harness-backup-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.zip",
                DefaultExtension = "zip",
                ShowOverwritePrompt = true,
                SuggestedStartLocation =
                    await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Documents),
                FileTypeChoices =
                [
                    new("Harness.NET backup archive") { Patterns = ["*.zip"] },
                ],
            });
            if (file is null)
            {
                return new(null, null);
            }

            string? localPath = file.TryGetLocalPath();
            return string.IsNullOrWhiteSpace(localPath)
                ? new(null, "The selected destination is not available as a local filesystem path.")
                : new(new(localPath), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(null, $"The save dialog could not be opened: {exception.Message}");
        }
    }
}
