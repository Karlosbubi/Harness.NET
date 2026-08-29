using System.Globalization;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Retrieval;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;

namespace Harness.Presentation.Avalonia;

internal sealed class TextEntryDialog : Window
{
    private readonly TextBox editor = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 260,
    };
    private readonly TextBlock validation = new();
    private readonly string requiredMessage;

    internal TextEntryDialog(
        string title,
        string label,
        string action,
        string requiredMessage)
    {
        this.requiredMessage = requiredMessage;
        Title = title;
        Width = 720;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(editor, label);
        AutomationProperties.SetName(validation, $"{title} validation");
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close();
        Button save = new() { Content = action };
        save.Click += (_, _) => Save();
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = label, FontWeight = FontWeight.SemiBold },
                editor,
                validation,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, save },
                },
            },
        };
    }

    internal string? Result { get; private set; }

    private void Save()
    {
        string content = editor.Text?.Trim() ?? string.Empty;
        if (content.Length == 0)
        {
            validation.Text = requiredMessage;
            return;
        }

        Result = content;
        Close();
    }
}

