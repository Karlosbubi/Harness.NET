using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Harness.Presentation.Avalonia;

internal sealed record WorkspaceFolderPath(string Value);

internal sealed record WorkspaceFolderPickerResult(
    WorkspaceFolderPath? Folder,
    string? Error);

internal interface IWorkspaceFolderPicker
{
    ValueTask<WorkspaceFolderPickerResult> PickAsync(
        TopLevel owner,
        WorkspaceFolderPath? currentFolder,
        CancellationToken cancellationToken = default);
}

internal sealed class AvaloniaWorkspaceFolderPicker : IWorkspaceFolderPicker
{
    public async ValueTask<WorkspaceFolderPickerResult> PickAsync(
        TopLevel owner,
        WorkspaceFolderPath? currentFolder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        cancellationToken.ThrowIfCancellationRequested();
        IStorageProvider storage = owner.StorageProvider;
        if (!storage.CanPickFolder)
        {
            return new(null, "This desktop does not provide a folder picker. Enter the repository path instead.");
        }

        try
        {
            IStorageFolder? start = await SuggestedStartAsync(storage, currentFolder);
            IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(new()
            {
                Title = "Open a .NET workspace",
                AllowMultiple = false,
                SuggestedStartLocation = start,
            });
            if (folders.Count == 0)
            {
                return new(null, null);
            }

            string? localPath = folders[0].TryGetLocalPath();
            return string.IsNullOrWhiteSpace(localPath)
                ? new(null, "The selected folder is not available as a local filesystem path.")
                : new(new(localPath), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(null, $"The folder picker could not be opened: {exception.Message}");
        }
    }

    private static async ValueTask<IStorageFolder?> SuggestedStartAsync(
        IStorageProvider storage,
        WorkspaceFolderPath? currentFolder)
    {
        if (currentFolder is { Value.Length: > 0 })
        {
            IStorageFolder? selected = await storage.TryGetFolderFromPathAsync(currentFolder.Value);
            if (selected is not null)
            {
                return selected;
            }
        }

        return await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Documents);
    }
}
