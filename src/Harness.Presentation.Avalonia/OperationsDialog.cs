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

    internal OperationsDialog(
        AvaloniaPresentationStore store,
        CancellationToken cancellationToken,
        IBackupFilePicker? filePicker = null)
    {
        this.store = store;
        this.cancellationToken = cancellationToken;
        this.filePicker = filePicker ?? new AvaloniaBackupFilePicker();
        Title = "Application operations";
        Width = 760;
        Height = 590;
        MinWidth = 620;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
        create.Click += async (_, _) => await ConfirmAndCreateAsync();
        browse.Click += async (_, _) => await ChooseDestinationAsync();
        subscription = store.States.Subscribe(state =>
            Dispatcher.UIThread.Post(() => Render(state.Operations)));
        Closed += (_, _) => subscription.Dispose();
    }

    private Control BuildContent()
    {
        Button close = new() { Content = "Close" };
        close.Click += (_, _) => Close();
        return new StackPanel
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
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { close },
                },
            },
        };
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
        result.Text = state.LastBackup is null
            ? "No backup has been created in this session."
            : Format(state.LastBackup);
        status.Text = state.IsBusy ? "Creating verified backup…" : state.Status ?? string.Empty;
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
