using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Harness.BusinessLogic.Operations;

namespace Harness.Presentation.Avalonia;

internal sealed class OperationsDialog : Window
{
    private readonly AvaloniaPresentationStore store;
    private readonly CancellationToken cancellationToken;
    private readonly IBackupFilePicker filePicker;
    private readonly IRestoreFilePicker restoreFilePicker;
    private readonly IDisposable subscription;
    private readonly TextBox destination = new();
    private readonly Button browse = new() { Content = "Choose…" };
    private readonly TextBox result = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 180,
    };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button create = new() { Content = "Create verified backup…" };
    private readonly TextBox restoreSource = new();
    private readonly Button restoreBrowse = new() { Content = "Choose…" };
    private readonly Button inspectRestore = new() { Content = "Inspect and verify archive" };
    private readonly Button stageRestore = new() { Content = "Stage verified restore…" };
    private readonly TextBox restoreResult = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 150,
    };

    internal OperationsDialog(
        AvaloniaPresentationStore store,
        CancellationToken cancellationToken,
        IBackupFilePicker? filePicker = null,
        IRestoreFilePicker? restoreFilePicker = null)
    {
        this.store = store;
        this.cancellationToken = cancellationToken;
        this.filePicker = filePicker ?? new AvaloniaBackupFilePicker();
        this.restoreFilePicker = restoreFilePicker ?? new AvaloniaRestoreFilePicker();
        Title = "Application operations";
        Width = 760;
        Height = 590;
        MinWidth = 620;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
        create.Click += async (_, _) => await ConfirmAndCreateAsync();
        browse.Click += async (_, _) => await ChooseDestinationAsync();
        restoreBrowse.Click += async (_, _) => await ChooseRestoreAsync();
        inspectRestore.Click += async (_, _) => await InspectRestoreAsync();
        stageRestore.Click += async (_, _) => await ConfirmAndStageRestoreAsync();
        subscription = store.States.Subscribe(state =>
            Dispatcher.UIThread.Post(() => Render(state.Operations)));
        Closed += (_, _) => subscription.Dispose();
    }

    private Control BuildContent()
    {
        Button close = new() { Content = "Close" };
        close.Click += (_, _) => Close();
        return new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 12,
                Children =
            {
                new TextBlock
                {
                    Text = "Application-state backup",
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = "Create a new, non-overwriting ZIP archive at an absolute path.",
                    TextWrapping = TextWrapping.Wrap,
                },
                DestinationRow(),
                new TextBlock
                {
                    Text = "The archive contains private prompts, workflow evidence, approvals, " +
                           "costs, and semantic state. It excludes credentials, logs, caches, model " +
                           "blobs, worktrees, and user repositories. Protect it like the original " +
                           "Harness.NET data directory.",
                    TextWrapping = TextWrapping.Wrap,
                },
                create,
                result,
                status,
                new Separator { Margin = new Thickness(0, 8) },
                new TextBlock
                {
                    Text = "Restore application state",
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = "First inspect a backup. Staging never changes the running app; the " +
                           "verified archive is rechecked and applied at the next start.",
                    TextWrapping = TextWrapping.Wrap,
                },
                RestoreSourceRow(),
                inspectRestore,
                restoreResult,
                stageRestore,
                new TextBlock
                {
                    Text = "Restore replaces private prompts, conversations, settings, approvals, " +
                           "cost and index state, and workbench layout. Credentials, repositories, " +
                           "worktrees, logs, caches, and model blobs are not in the archive. A local " +
                           "rollback is retained if current state exists.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { close },
                },
            },
            }
        };
    }

    private Control RestoreSourceRow()
    {
        AutomationProperties.SetName(restoreSource, "Restore archive path");
        Grid row = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        row.Children.Add(restoreSource);
        restoreBrowse.SetValue(Grid.ColumnProperty, 1);
        row.Children.Add(restoreBrowse);
        return row;
    }

    private async Task ChooseRestoreAsync()
    {
        RestoreFilePickerResult picked = await restoreFilePicker.PickAsync(this, cancellationToken);
        if (picked.Error is not null)
        {
            status.Text = picked.Error;
        }
        else if (picked.File is not null)
        {
            restoreSource.Text = picked.File.Value;
            await InspectRestoreAsync();
        }
    }

    private async Task InspectRestoreAsync() => await store.InspectApplicationRestoreAsync(
        new(restoreSource.Text?.Trim() ?? string.Empty), cancellationToken);

    private async Task ConfirmAndStageRestoreAsync()
    {
        ApplicationRestoreView? restore = store.Current.Operations.InspectedRestore;
        string source = restoreSource.Text?.Trim() ?? string.Empty;
        if (restore is null || !restore.Archive.Value.Equals(source, StringComparison.Ordinal))
        {
            status.Text = "Inspect this exact archive before staging it.";
            return;
        }

        if (await new RestoreConfirmationDialog(restore).ShowDialog<bool>(this))
        {
            await store.StageApplicationRestoreAsync(restore, cancellationToken);
        }
    }

    private Control DestinationRow()
    {
        AutomationProperties.SetName(destination, "Backup archive path");
        AutomationProperties.SetName(browse, "Choose backup archive destination");
        Grid row = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 8 };
        row.Children.Add(destination);
        browse.SetValue(Grid.ColumnProperty, 1);
        row.Children.Add(browse);
        return row;
    }

    private async Task ChooseDestinationAsync()
    {
        BackupFilePickerResult result = await filePicker.PickAsync(this, cancellationToken);
        if (result.Error is { } error)
        {
            status.Text = error;
            return;
        }

        if (result.File is not { } file)
        {
            return;
        }

        destination.Text = file.Value;

        // The backup service refuses to overwrite, so say that here rather than at creation.
        status.Text = File.Exists(file.Value)
            ? "That archive already exists. Choose a destination that does not exist yet."
            : string.Empty;
    }

    private async Task ConfirmAndCreateAsync()
    {
        string path = destination.Text?.Trim() ?? string.Empty;
        if (path.Length == 0)
        {
            status.Text = "Enter an absolute destination ending in .zip.";
            return;
        }

        BackupConfirmationDialog confirmation = new(path);
        if (await confirmation.ShowDialog<bool>(this))
        {
            await store.CreateApplicationBackupAsync(new(path), cancellationToken);
        }
    }

    private void Render(ApplicationOperationsState state)
    {
        create.IsEnabled = !state.IsBusy;
        inspectRestore.IsEnabled = !state.IsBusy;
        stageRestore.IsEnabled = !state.IsBusy && state.InspectedRestore is not null &&
            state.PendingRestore is null;
        result.Text = state.LastBackup is null
            ? "No backup has been created in this session."
            : Format(state.LastBackup);
        status.Text = state.Status ?? string.Empty;
        if (state.InspectedRestore is not null)
        {
            restoreSource.Text = state.InspectedRestore.Archive.Value;
        }
        restoreResult.Text = state.PendingRestore is not null
            ? "RESTORE PENDING — restart Harness.NET to apply it.\n\n" + Format(state.PendingRestore)
            : state.InspectedRestore is null
                ? "No restore archive has been inspected in this session."
                : Format(state.InspectedRestore);
    }

    private static string Format(ApplicationBackupView backup) => string.Join(
        '\n',
        $"Archive: {backup.Archive.Value}",
        $"Archive SHA-256: {backup.ArchiveSha256.Value}",
        $"Database SHA-256: {backup.DatabaseSha256.Value}",
        $"Database bytes: {backup.DatabaseBytes.Value}",
        backup.WorkbenchLayoutSha256 is null
            ? "Workbench layout: not present"
            : $"Workbench layout: {backup.WorkbenchLayoutBytes?.Value} bytes · " +
              $"SHA-256 {backup.WorkbenchLayoutSha256.Value}",
        $"Schema version: {backup.SchemaVersion.Value}",
        $"Created: {backup.CreatedAt:O}");

    private static string Format(ApplicationRestoreView restore) => string.Join('\n',
        $"Archive: {restore.Archive.Value}",
        $"Archive SHA-256: {restore.ArchiveSha256.Value}",
        $"Database SHA-256: {restore.DatabaseSha256.Value}",
        $"Database bytes: {restore.DatabaseBytes.Value}",
        restore.WorkbenchLayoutSha256 is null
            ? "Workbench layout: not present (current layout will be removed)"
            : $"Workbench layout: {restore.WorkbenchLayoutBytes?.Value} bytes · SHA-256 {restore.WorkbenchLayoutSha256.Value}",
        $"Schema version: {restore.SchemaVersion.Value}",
        $"Created: {restore.CreatedAt:O}");
}

internal sealed class RestoreConfirmationDialog : Window
{
    internal RestoreConfirmationDialog(ApplicationRestoreView restore)
    {
        Title = "Confirm private-state restore";
        Width = 650;
        Height = 390;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close(false);
        Button stage = new() { Content = "Stage restore for next restart" };
        stage.Click += (_, _) => Close(true);
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Replace Harness.NET private state?", FontSize = 18,
                    FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = $"Schema {restore.SchemaVersion.Value} · created {restore.CreatedAt:O}\n" +
                    $"Archive SHA-256: {restore.ArchiveSha256.Value}", TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "Changes made after staging will also be replaced on restart. " +
                    "The app revalidates the staged database and layout before replacement and keeps " +
                    "the previous local state as rollback material. This action does not restore " +
                    "credentials or repositories.", TextWrapping = TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8,
                    Children = { cancel, stage } },
            },
        };
    }
}

internal sealed class BackupConfirmationDialog : Window
{
    internal BackupConfirmationDialog(string destination)
    {
        Title = "Confirm private-state export";
        Width = 620;
        Height = 320;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close(false);
        Button create = new() { Content = "Create sensitive backup" };
        create.Click += (_, _) => Close(true);
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = "Create this sensitive application-state archive?",
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock { Text = destination, TextWrapping = TextWrapping.Wrap },
                new TextBlock
                {
                    Text = "The destination must not already exist. Harness.NET creates a consistent " +
                           "SQLite snapshot, verifies database integrity, records hashes and schema, " +
                           "and publishes the archive atomically.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, create },
                },
            },
        };
    }
}
