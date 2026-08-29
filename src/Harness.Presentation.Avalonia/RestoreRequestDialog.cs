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

internal sealed class RestoreRequestDialog : Window
{
    private readonly TextBox correlation = new();
    private readonly TextBox rationale = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 150,
    };
    private readonly TextBlock validation = new() { TextWrapping = TextWrapping.Wrap };

    internal RestoreRequestDialog()
    {
        Title = "Request one restore authorization";
        Width = 680;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        correlation.Text = Guid.NewGuid().ToString("N");
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close();
        Button save = new() { Content = "Record pending request" };
        save.Click += (_, _) => Save();
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "Restore requires a unique correlation shared by exactly one later restore " +
                           "tool call. It does not authorize other correlations, targets, or capabilities.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock { Text = "Correlation identifier" },
                correlation,
                new TextBlock { Text = "Why is dependency restore required?" },
                rationale,
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

    internal RestoreRequestInput? Result { get; private set; }

    private void Save()
    {
        string correlationValue = correlation.Text?.Trim() ?? string.Empty;
        string rationaleValue = rationale.Text?.Trim() ?? string.Empty;
        if (correlationValue.Length == 0 || rationaleValue.Length == 0)
        {
            validation.Text = "Correlation and rationale are required.";
            return;
        }

        Result = new(new(correlationValue), rationaleValue);
        Close();
    }
}

internal sealed record RestoreRequestInput(
    ToolCorrelationId CorrelationId,
    string Rationale);

