using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using Harness.BusinessLogic.Framework;

namespace Harness.Presentation.Avalonia;

internal sealed class FrameworkDialog : Window
{
    private readonly AvaloniaPresentationStore store;
    private readonly CancellationToken cancellationToken;
    private readonly IDisposable subscription;
    private readonly ContentControl effective = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button refresh = new() { Content = "Refresh" };
    private readonly Button edit = new() { Content = "Edit private overlay…" };

    internal FrameworkDialog(
        AvaloniaPresentationStore store,
        CancellationToken cancellationToken)
    {
        this.store = store;
        this.cancellationToken = cancellationToken;
        Title = "Effective engineering framework";
        Width = 920;
        Height = 720;
        MinWidth = 680;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
        refresh.Click += async (_, _) => await store.RefreshFrameworkAsync(cancellationToken);
        edit.Click += async (_, _) => await EditOverlayAsync();
        Opened += async (_, _) => await store.RefreshFrameworkAsync(cancellationToken);
        subscription = store.States.Subscribe(state =>
            Dispatcher.UIThread.Post(() => Render(state.Framework)));
        Closed += (_, _) => subscription.Dispose();
    }

    private Control BuildContent()
    {
        Grid root = new()
        {
            RowDefinitions = new("Auto,*,Auto,Auto"),
            Margin = new Thickness(20),
            RowSpacing = 12,
        };
        root.Children.Add(new TextBlock
        {
            Text = "Resolved rules, guidance, provenance, privacy, locks, and validation issues",
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetRow(effective, 1);
        root.Children.Add(effective);
        Grid.SetRow(status, 2);
        root.Children.Add(status);

        Button close = new() { Content = "Close" };
        close.Click += (_, _) => Close();
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { refresh, edit, close },
        };
        Grid.SetRow(actions, 3);
        root.Children.Add(actions);
        return root;
    }

    private async Task EditOverlayAsync()
    {
        string content = store.Current.Framework.Snapshot?.Documents
            .FirstOrDefault(document => document.Layer == "private-workspace")
            ?.Content ?? string.Empty;
        PrivateFrameworkOverlayDialog dialog = new(content);
        string? updated = await dialog.ShowDialog<string?>(this);
        if (updated is not null)
        {
            await store.SetPrivateFrameworkOverlayAsync(updated, cancellationToken);
        }
    }

    private void Render(FrameworkManagementState state)
    {
        refresh.IsEnabled = !state.IsBusy;
        edit.IsEnabled = !state.IsBusy && state.Snapshot is not null;
        effective.Content = FrameworkContentView.Create(state.Snapshot);
        status.Text = state.IsBusy ? "Resolving framework…" : state.Status ?? string.Empty;
    }
}

internal sealed class PrivateFrameworkOverlayDialog : Window
{
    private readonly TextEditor editor = CodeEditorView.Create(
        isReadOnly: false,
        wordWrap: true,
        showLineNumbers: true);

    internal PrivateFrameworkOverlayDialog(string content)
    {
        Title = "Private workspace framework overlay";
        Width = 820;
        Height = 620;
        MinWidth = 620;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        editor.Text = content;
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close(null);
        Button save = new() { Content = "Save private overlay" };
        save.Click += (_, _) => Close(editor.Text ?? string.Empty);

        Grid root = new()
        {
            RowDefinitions = new("Auto,*,Auto"),
            Margin = new Thickness(20),
            RowSpacing = 12,
        };
        root.Children.Add(new TextBlock
        {
            Text = "This Markdown stays in Harness.NET private storage for the active workspace. " +
                   "It does not modify AGENTS.md or add metadata to the repository. Saving empty " +
                   "content removes the overlay.",
            TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetRow(editor, 1);
        root.Children.Add(editor);
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, save },
        };
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);
        Content = root;
    }
}
