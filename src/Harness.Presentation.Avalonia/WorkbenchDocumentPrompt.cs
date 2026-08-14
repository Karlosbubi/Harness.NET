using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using Harness.BusinessLogic.Inspection;

namespace Harness.Presentation.Avalonia;

internal enum WorkbenchUnsavedDecision
{
    Cancel,
    Save,
    Discard,
}

internal enum WorkbenchConflictDecision
{
    Cancel,
    Reload,
    Overwrite,
}

internal enum WorkbenchDocumentTransition
{
    Close,
    Switch,
    Reload,
    Exit,
}

internal sealed record WorkbenchUnsavedPrompt(
    string Path,
    WorkbenchDocumentTransition Transition);

internal sealed record WorkbenchConflictPrompt(
    string Path,
    bool FileWasDeleted);

internal interface IWorkbenchDocumentPrompt
{
    ValueTask<WorkbenchUnsavedDecision> DecideUnsavedAsync(
        WorkbenchUnsavedPrompt prompt,
        Window? owner);

    ValueTask<WorkbenchConflictDecision> DecideConflictAsync(
        WorkbenchConflictPrompt prompt,
        Window? owner);

    ValueTask<bool> ConfirmGitDestructiveAsync(
        DeveloperGitDestructivePreviewView preview,
        Window? owner);

    ValueTask<DeveloperGitCommitDraft?> CollectGitCommitAsync(Window? owner);

    ValueTask<bool> ConfirmGitCommitAsync(
        DeveloperGitCommitPreviewView preview,
        Window? owner);
}

internal sealed record DeveloperGitCommitDraft(
    DeveloperGitCommitMessage Message,
    DeveloperGitCommitAction Action,
    DeveloperGitCommitHookPolicy HookPolicy);

internal sealed class AvaloniaWorkbenchDocumentPrompt : IWorkbenchDocumentPrompt
{
    public async ValueTask<WorkbenchUnsavedDecision> DecideUnsavedAsync(
        WorkbenchUnsavedPrompt prompt,
        Window? owner)
    {
        if (owner is null)
        {
            return WorkbenchUnsavedDecision.Cancel;
        }

        UnsavedDocumentDialog dialog = new(prompt);
        return await dialog.ShowDialog<WorkbenchUnsavedDecision>(owner);
    }

    public async ValueTask<WorkbenchConflictDecision> DecideConflictAsync(
        WorkbenchConflictPrompt prompt,
        Window? owner)
    {
        if (owner is null)
        {
            return WorkbenchConflictDecision.Cancel;
        }

        DocumentConflictDialog dialog = new(prompt);
        return await dialog.ShowDialog<WorkbenchConflictDecision>(owner);
    }

    public async ValueTask<bool> ConfirmGitDestructiveAsync(
        DeveloperGitDestructivePreviewView preview,
        Window? owner)
    {
        if (owner is null) return false;
        return await new GitDestructiveConfirmationDialog(preview).ShowDialog<bool>(owner);
    }

    public async ValueTask<DeveloperGitCommitDraft?> CollectGitCommitAsync(Window? owner)
    {
        if (owner is null) return null;
        return await new GitCommitComposeDialog().ShowDialog<DeveloperGitCommitDraft?>(owner);
    }

    public async ValueTask<bool> ConfirmGitCommitAsync(
        DeveloperGitCommitPreviewView preview,
        Window? owner)
    {
        if (owner is null) return false;
        return await new GitCommitConfirmationDialog(preview).ShowDialog<bool>(owner);
    }
}

internal sealed class GitCommitComposeDialog : Window
{
    internal GitCommitComposeDialog()
    {
        Title = "Compose Git commit";
        Width = 620;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, "Compose developer Git commit");
        Content = BuildContent();
    }

    private Control BuildContent()
    {
        var message = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        var amend = new CheckBox { Content = "Amend the current HEAD commit" };
        var bypassHooks = new CheckBox { Content = "Bypass configured commit hooks (--no-verify)" };
        Button review = new() { Content = "Review staged commit", IsDefault = true, MinWidth = 150 };
        Button cancel = new() { Content = "Cancel", IsCancel = true, MinWidth = 88 };
        AutomationProperties.SetName(message, "Developer Git commit message");
        AutomationProperties.SetName(amend, "Amend current Git HEAD");
        AutomationProperties.SetName(bypassHooks, "Bypass configured Git commit hooks");
        AutomationProperties.SetName(review, "Review exact staged Git commit");
        AutomationProperties.SetName(cancel, "Cancel developer Git commit");
        message.TextChanged += (_, _) => review.IsEnabled = !string.IsNullOrWhiteSpace(message.Text);
        review.IsEnabled = false;
        review.Click += (_, _) => Close(new DeveloperGitCommitDraft(
            new(message.Text ?? string.Empty),
            amend.IsChecked == true ? DeveloperGitCommitAction.Amend : DeveloperGitCommitAction.Create,
            bypassHooks.IsChecked == true
                ? DeveloperGitCommitHookPolicy.BypassHooks
                : DeveloperGitCommitHookPolicy.RunConfiguredHooks));
        cancel.Click += (_, _) => Close(null);
        return new Grid
        {
            Margin = new Thickness(24),
            RowDefinitions = new("Auto,*,Auto,Auto,Auto"),
            RowSpacing = 12,
            Children =
            {
                new TextBlock { Text = "Commit message", FontWeight = FontWeight.SemiBold },
                AtRow(message, 1),
                AtRow(amend, 2),
                AtRow(bypassHooks, 3),
                AtRow(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, review },
                }, 4),
            },
        };
    }

    private static T AtRow<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }
}

internal sealed class GitCommitConfirmationDialog : Window
{
    internal GitCommitConfirmationDialog(DeveloperGitCommitPreviewView preview)
    {
        Title = preview.Action == DeveloperGitCommitAction.Amend ? "Confirm Git amend" : "Confirm Git commit";
        Width = 780;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, "Review exact developer Git commit");
        Content = BuildContent(preview);
    }

    private void Confirm() => Close(true);

    private Control BuildContent(DeveloperGitCommitPreviewView preview)
    {
        TextEditor diff = CodeEditorView.Create(preview.StagedDiff, true, false, true, "staged.diff");
        AutomationProperties.SetName(diff, "Exact staged Git diff");
        Button confirm = new()
        {
            Content = preview.Action == DeveloperGitCommitAction.Amend ? "Amend commit" : "Create commit",
            MinWidth = 120,
        };
        Button cancel = new() { Content = "Cancel", IsCancel = true, MinWidth = 88 };
        AutomationProperties.SetName(confirm, "Confirm exact developer Git commit");
        AutomationProperties.SetName(cancel, "Cancel developer Git commit preview");
        confirm.Click += (_, _) => Confirm();
        cancel.Click += (_, _) => Close(false);
        string hooks = preview.HookPolicy == DeveloperGitCommitHookPolicy.RunConfiguredHooks
            ? "Configured Git hooks will run."
            : "Configured Git hooks will be bypassed.";
        return new Grid
        {
            Margin = new Thickness(20),
            RowDefinitions = new("Auto,Auto,Auto,*,Auto"),
            RowSpacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = $"{preview.Action} on {preview.Branch} at {preview.HeadSha ?? "unborn"}",
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                AtRow(new TextBlock
                {
                    Text = $"Author: {preview.AuthorName} <{preview.AuthorEmail}>\n{hooks}\n" +
                           $"Paths: {string.Join(", ", preview.StagedPaths.Select(path => path.Value))}\n\n" +
                           $"{preview.Consequence}\n{preview.Recovery}\n\n{preview.Message.Value}",
                    TextWrapping = TextWrapping.Wrap,
                }, 1),
                AtRow(new TextBlock { Text = "Exact staged diff", FontWeight = FontWeight.SemiBold }, 2),
                AtRow(diff, 3),
                AtRow(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, confirm },
                }, 4),
            },
        };
    }

    private static T AtRow<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }
}

internal sealed class GitDestructiveConfirmationDialog : Window
{
    internal GitDestructiveConfirmationDialog(DeveloperGitDestructivePreviewView preview)
    {
        Title = "Confirm destructive Git action";
        Width = 620;
        MaxHeight = 720;
        SizeToContent = SizeToContent.Height;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, "Confirm destructive Git action");
        Content = BuildContent(preview);
    }

    private Control BuildContent(DeveloperGitDestructivePreviewView preview)
    {
        Button confirm = new() { Content = "Apply destructive action", MinWidth = 170, IsEnabled = false };
        Button cancel = new() { Content = "Cancel", MinWidth = 88 };
        var acknowledgement = new CheckBox
        {
            Content = "I understand that Git does not guarantee recovery for these paths.",
        };
        AutomationProperties.SetName(acknowledgement, "Acknowledge destructive Git consequences");
        AutomationProperties.SetName(confirm, $"Confirm {preview.Title}");
        AutomationProperties.SetName(cancel, "Cancel destructive Git action");
        acknowledgement.IsCheckedChanged += (_, _) => confirm.IsEnabled = acknowledgement.IsChecked == true;
        confirm.Click += (_, _) => Close(true);
        cancel.Click += (_, _) => Close(false);
        ItemsControl paths = new()
        {
            ItemsSource = preview.Paths.Select(path => path.Value).ToArray(),
            MaxHeight = 220,
        };
        AutomationProperties.SetName(paths, "Exact destructive Git paths");
        return new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = preview.Title,
                        FontSize = 18,
                        FontWeight = FontWeight.SemiBold,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock { Text = preview.Consequence, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "Exact paths", FontWeight = FontWeight.SemiBold },
                    paths,
                    new Border
                    {
                        BorderBrush = Brushes.Orange,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(12),
                        Child = new TextBlock { Text = preview.Recovery, TextWrapping = TextWrapping.Wrap },
                    },
                    acknowledgement,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, confirm },
                    },
                },
            },
        };
    }
}

internal sealed class UnsavedDocumentDialog : Window
{
    internal UnsavedDocumentDialog(WorkbenchUnsavedPrompt prompt)
    {
        Title = "Unsaved source changes";
        Width = 500;
        SizeToContent = SizeToContent.Height;
        MinHeight = 220;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent(prompt);
        AutomationProperties.SetName(this, "Unsaved source changes");
    }

    private Control BuildContent(WorkbenchUnsavedPrompt prompt)
    {
        string action = prompt.Transition switch
        {
            WorkbenchDocumentTransition.Close => "closing this document",
            WorkbenchDocumentTransition.Switch => "switching documents",
            WorkbenchDocumentTransition.Reload => "reloading from the worktree",
            WorkbenchDocumentTransition.Exit => "exiting Harness.NET",
            _ => throw new ArgumentOutOfRangeException(nameof(prompt)),
        };
        TextBlock heading = new()
        {
            Text = "Save your changes?",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
        };
        TextBlock explanation = new()
        {
            Text = $"{prompt.Path} has unsaved changes. Choose what to do before {action}.",
            TextWrapping = TextWrapping.Wrap,
        };
        Button save = new() { Content = "Save", MinWidth = 88 };
        Button discard = new() { Content = "Discard", MinWidth = 88 };
        Button cancel = new() { Content = "Cancel", MinWidth = 88 };
        AutomationProperties.SetName(save, $"Save changes to {prompt.Path}");
        AutomationProperties.SetName(discard, $"Discard changes to {prompt.Path}");
        AutomationProperties.SetName(cancel, "Cancel document transition");
        save.Click += (_, _) => Close(WorkbenchUnsavedDecision.Save);
        discard.Click += (_, _) => Close(WorkbenchUnsavedDecision.Discard);
        cancel.Click += (_, _) => Close(WorkbenchUnsavedDecision.Cancel);
        return new StackPanel
        {
            Margin = new(24),
            Spacing = 18,
            Children =
            {
                heading,
                explanation,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, discard, save },
                },
            },
        };
    }
}

internal sealed class DocumentConflictDialog : Window
{
    internal DocumentConflictDialog(WorkbenchConflictPrompt prompt)
    {
        Title = "Source file changed";
        Width = 540;
        SizeToContent = SizeToContent.Height;
        MinHeight = 250;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent(prompt);
        AutomationProperties.SetName(this, "Source file save conflict");
    }

    private Control BuildContent(WorkbenchConflictPrompt prompt)
    {
        TextBlock heading = new()
        {
            Text = prompt.FileWasDeleted ? "The source file was deleted" : "The source file changed on disk",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
        };
        TextBlock explanation = new()
        {
            Text = prompt.FileWasDeleted
                ? $"{prompt.Path} no longer exists in the goal worktree. Reload closes this stale buffer; Recreate writes your current editor content as a new file."
                : $"{prompt.Path} changed after it was opened. Reload uses the current worktree content; Overwrite replaces that exact current version with your editor content. Both choices remain compare-and-swap protected.",
            TextWrapping = TextWrapping.Wrap,
        };
        Button reload = new() { Content = "Reload", MinWidth = 88 };
        Button overwrite = new()
        {
            Content = prompt.FileWasDeleted ? "Recreate" : "Overwrite",
            MinWidth = 88,
        };
        Button cancel = new() { Content = "Keep editing", MinWidth = 100 };
        AutomationProperties.SetName(reload, $"Reload {prompt.Path} from the goal worktree");
        AutomationProperties.SetName(
            overwrite,
            prompt.FileWasDeleted ? $"Recreate {prompt.Path}" : $"Overwrite changed {prompt.Path}");
        AutomationProperties.SetName(cancel, "Keep unsaved editor content");
        reload.Click += (_, _) => Close(WorkbenchConflictDecision.Reload);
        overwrite.Click += (_, _) => Close(WorkbenchConflictDecision.Overwrite);
        cancel.Click += (_, _) => Close(WorkbenchConflictDecision.Cancel);
        return new StackPanel
        {
            Margin = new(24),
            Spacing = 18,
            Children =
            {
                heading,
                explanation,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, reload, overwrite },
                },
            },
        };
    }
}
