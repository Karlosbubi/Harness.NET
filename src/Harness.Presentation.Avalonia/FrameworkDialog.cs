using System.Reactive.Linq;
using System.Text;
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
    private readonly TextEditor effective = CodeEditorView.Create(showLineNumbers: false);
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
        effective.Text = state.Snapshot is null
            ? "Select an active workspace and refresh its effective framework."
            : Format(state.Snapshot);
        status.Text = state.IsBusy ? "Resolving framework…" : state.Status ?? string.Empty;
    }

    private static string Format(FrameworkSnapshot snapshot)
    {
        StringBuilder text = new();
        text.AppendLine(snapshot.IsValid ? "VALID" : "ATTENTION REQUIRED")
            .AppendLine()
            .AppendLine("EFFECTIVE RULES");
        if (snapshot.Rules.Count == 0)
        {
            text.AppendLine("(none)");
        }

        foreach (EffectiveFrameworkRule rule in snapshot.Rules)
        {
            text.Append(rule.IsLocked ? "[locked] " : "          ")
                .Append(rule.Key)
                .Append(" = ")
                .AppendLine(rule.Value)
                .Append("          layer: ")
                .Append(rule.Layer)
                .Append(" | source: ")
                .AppendLine(rule.Source);
        }

        text.AppendLine().AppendLine("GUIDANCE DOCUMENTS");
        if (snapshot.Documents.Count == 0)
        {
            text.AppendLine("(none)");
        }

        foreach (FrameworkDocumentView document in snapshot.Documents)
        {
            text.Append('[')
                .Append(document.Layer)
                .Append(" | precedence ")
                .Append(document.Precedence)
                .Append(document.IsPrivate ? " | private" : " | shared")
                .Append("] ")
                .AppendLine(document.Source)
                .AppendLine(document.Content)
                .AppendLine();
        }

        text.AppendLine("ISSUES");
        if (snapshot.Issues.Count == 0)
        {
            text.AppendLine("(none)");
        }

        foreach (FrameworkIssue issue in snapshot.Issues)
        {
            text.Append('[').Append(issue.Code).Append("] ").AppendLine(issue.Message);
            if (issue.Key is not null)
            {
                text.Append("          key: ").AppendLine(issue.Key);
            }

            if (issue.Sources.Count > 0)
            {
                text.Append("          sources: ")
                    .AppendLine(string.Join(", ", issue.Sources));
            }
        }

        return text.ToString();
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
