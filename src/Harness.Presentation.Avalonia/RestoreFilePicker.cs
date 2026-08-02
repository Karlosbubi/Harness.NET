using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Harness.Presentation.Avalonia;

internal sealed record RestoreFilePath(string Value);
internal sealed record RestoreFilePickerResult(RestoreFilePath? File, string? Error);

internal interface IRestoreFilePicker
{
    ValueTask<RestoreFilePickerResult> PickAsync(
        TopLevel owner,
        CancellationToken cancellationToken = default);
}

internal sealed class AvaloniaRestoreFilePicker : IRestoreFilePicker
{
    public async ValueTask<RestoreFilePickerResult> PickAsync(
        TopLevel owner,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!owner.StorageProvider.CanOpen)
        {
            return new(null, "This desktop does not provide an open dialog. Enter the archive path instead.");
        }

        try
        {
            IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new()
            {
                Title = "Inspect an application-state backup",
                AllowMultiple = false,
                FileTypeFilter = [new("Harness.NET backup archive") { Patterns = ["*.zip"] }],
            });
            string? path = files.FirstOrDefault()?.TryGetLocalPath();
            return string.IsNullOrWhiteSpace(path) ? new(null, null) : new(new(path), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(null, $"The open dialog could not be opened: {exception.Message}");
        }
    }
}
