using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Mutations;

namespace Harness.Presentation.Avalonia;

internal sealed record PendingWorkbenchRename(
    RenameSymbolPreviewRequest Request,
    WorkbenchCodeRenamePreviewView Preview);

internal sealed class RenameNameDialog : Window
{
    private readonly TextBox name = new();
    private readonly TextBlock validation = new();

    internal RenameNameDialog()
    {
        Title = "Rename symbol";
        Width = 440;
        Height = 190;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(name, "New symbol identifier");
        AutomationProperties.SetName(validation, "Rename identifier validation");
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close();
        Button preview = new() { Content = "Preview rename" };
        preview.Classes.Add("primary");
        preview.Click += (_, _) => Accept();
        Content = new StackPanel
        {
            Margin = new global::Avalonia.Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "New identifier", FontWeight = FontWeight.SemiBold },
                name,
                validation,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, preview },
                },
            },
        };
        Opened += (_, _) => name.Focus();
    }

    internal string? Result { get; private set; }

    private void Accept()
    {
        string value = name.Text?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            validation.Text = "Enter a new C# identifier.";
            return;
        }

        Result = value;
        Close();
    }
}

internal sealed class RenamePreviewDialog : Window
{
    internal RenamePreviewDialog(WorkbenchCodeRenamePreviewView preview)
    {
        Title = "Preview symbol rename";
        Width = 720;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        bool ready = preview.Disposition is WorkbenchCodeTransformationDisposition.Ready &&
            preview.Fingerprint is not null;
        ListBox files = new()
        {
            ItemsSource = preview.Edits.Select(edit =>
                $"{edit.Path.Value} · {edit.ReplacementCount} replacement(s) · baseline {edit.BaselineHash.Value[..12]}")
                .ToArray(),
        };
        AutomationProperties.SetName(files, "Affected rename files");
        TextBlock details = new()
        {
            TextWrapping = TextWrapping.Wrap,
            Text = ready
                ? $"{preview.Symbol?.Value}\n\nRename to {preview.NewName.Value} in {preview.Edits.Count} file(s)."
                : string.Join('\n', preview.Conflicts.Select(conflict => conflict.Message.Value)
                    .Concat(preview.Issues.Select(issue => issue.Message.Value))),
        };
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close(false);
        Button apply = new() { Content = "Apply rename", IsEnabled = ready };
        apply.Classes.Add("primary");
        apply.Click += (_, _) => Close(true);
        Content = new Grid
        {
            Margin = new global::Avalonia.Thickness(20),
            RowDefinitions = new("Auto,*,Auto"),
            RowSpacing = 12,
            Children =
            {
                details,
                files,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, apply },
                },
            },
        };
        Grid.SetRow(files, 1);
        Grid.SetRow((Control)((Grid)Content).Children[2], 2);
    }
}
