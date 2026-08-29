using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Goals;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed class ProblemsTool
{
    private readonly Func<WorkbenchCodeDiagnostic, GoalId?, ValueTask> navigate;
    private readonly Dictionary<string, DiagnosticEntry> diagnostics = new(StringComparer.Ordinal);
    private readonly ListBox problems = new();
    private readonly StatusIndicator status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox showWarnings = new() { Content = "Warnings", IsChecked = true };
    private readonly CheckBox showInformation = new() { Content = "Info", IsChecked = true };
    private readonly CheckBox showHidden = new() { Content = "Hidden", IsChecked = false };

    internal ProblemsTool(Func<WorkbenchCodeDiagnostic, GoalId?, ValueTask> navigate)
    {
        this.navigate = navigate;
        Content = BuildContent();
    }

    internal Control Content { get; }
    internal ListBox List => problems;
    internal TextBlock Status => status;

    internal void Set(string documentId, GoalId? goalId, WorkbenchCodeDiagnosticView view)
    {
        diagnostics[documentId] = new(goalId, view);
        Render();
    }

    internal void Remove(string documentId)
    {
        if (diagnostics.Remove(documentId)) Render();
    }

    internal void Clear()
    {
        diagnostics.Clear();
        problems.ItemsSource = Array.Empty<ProblemChoice>();
        status.Message = "Open a .NET source file to load compiler diagnostics.";
    }

    internal void ToggleWarnings() => showWarnings.IsChecked = showWarnings.IsChecked is not true;

    internal void ToggleInformation() =>
        showInformation.IsChecked = showInformation.IsChecked is not true;

    internal void ToggleHidden() => showHidden.IsChecked = showHidden.IsChecked is not true;

    private Control BuildContent()
    {
        Grid grid = new()
        {
            RowDefinitions = new("Auto,*"),
            Margin = new(10),
            RowSpacing = 8,
        };
        Grid heading = new()
        {
            ColumnDefinitions = new("*,Auto,Auto,Auto"),
            ColumnSpacing = 10,
            Children = { status },
        };
        status.Message = "Open a .NET source file to load compiler diagnostics.";
        AutomationProperties.SetName(status, "Code intelligence status");
        AutomationProperties.SetName(showWarnings, "Show warning diagnostics");
        AutomationProperties.SetName(showInformation, "Show information diagnostics");
        AutomationProperties.SetName(showHidden, "Show hidden diagnostics");
        Grid.SetColumn(showWarnings, 1);
        Grid.SetColumn(showInformation, 2);
        Grid.SetColumn(showHidden, 3);
        heading.Children.Add(showWarnings);
        heading.Children.Add(showInformation);
        heading.Children.Add(showHidden);
        showWarnings.IsCheckedChanged += (_, _) => Render();
        showInformation.IsCheckedChanged += (_, _) => Render();
        showHidden.IsCheckedChanged += (_, _) => Render();
        grid.Children.Add(heading);
        AutomationProperties.SetName(problems, "Compiler and analyzer problems");
        problems.SelectionChanged += async (_, _) =>
        {
            if (problems.SelectedItem is ProblemChoice choice)
                await navigate(choice.Diagnostic, choice.GoalId);
        };
        Grid.SetRow(problems, 1);
        grid.Children.Add(problems);
        return grid;
    }

    private void Render()
    {
        ProblemChoice[] choices = diagnostics.Values
            .SelectMany(entry => entry.View.Diagnostics.Select(diagnostic =>
                new ProblemChoice(diagnostic, entry.GoalId)))
            .Where(choice => choice.Diagnostic.Severity switch
            {
                WorkbenchCodeDiagnosticSeverity.Error => true,
                WorkbenchCodeDiagnosticSeverity.Warning => showWarnings.IsChecked is true,
                WorkbenchCodeDiagnosticSeverity.Information => showInformation.IsChecked is true,
                WorkbenchCodeDiagnosticSeverity.Hidden => showHidden.IsChecked is true,
                _ => false,
            })
            .OrderByDescending(choice => choice.Diagnostic.Severity)
            .ThenBy(choice => choice.Diagnostic.Path.Value, StringComparer.Ordinal)
            .ThenBy(choice => choice.Diagnostic.Range.Start.Line)
            .Take(5_000)
            .ToArray();
        problems.ItemsSource = choices;
        int errors = choices.Count(choice =>
            choice.Diagnostic.Severity is WorkbenchCodeDiagnosticSeverity.Error);
        int warnings = choices.Count(choice =>
            choice.Diagnostic.Severity is WorkbenchCodeDiagnosticSeverity.Warning);
        WorkbenchCodeDiagnosticView? unavailable = diagnostics.Values
            .Select(entry => entry.View)
            .FirstOrDefault(result => result.State is WorkbenchCodeResultState.Degraded or
                WorkbenchCodeResultState.Failed);
        status.Message = unavailable?.Issues.FirstOrDefault() is { } issue
            ? $"Code intelligence {unavailable.State.ToString().ToLowerInvariant()} · " +
              issue.Message.Value
            : choices.Length == 0
                ? "No compiler or analyzer problems in the active buffers."
                : $"{errors:N0} error(s), {warnings:N0} warning(s), " +
                  $"{choices.Length - errors - warnings:N0} other finding(s).";
    }

    private sealed record DiagnosticEntry(GoalId? GoalId, WorkbenchCodeDiagnosticView View);

    private sealed record ProblemChoice(WorkbenchCodeDiagnostic Diagnostic, GoalId? GoalId)
    {
        public override string ToString()
        {
            int line = Diagnostic.Range.Start.Line + 1;
            int column = Diagnostic.Range.Start.Character + 1;
            return $"{Diagnostic.Severity} {Diagnostic.Id.Value}  " +
                   $"{Diagnostic.Path.Value}:{line}:{column}  {Diagnostic.Message.Value}";
        }
    }
}
