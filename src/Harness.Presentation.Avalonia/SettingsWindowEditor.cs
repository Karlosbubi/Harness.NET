using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Appearance;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Research;
using Harness.BusinessLogic.VisualCapture;

namespace Harness.Presentation.Avalonia;

internal sealed partial class SettingsWindow
{
    private Control EditorPage()
    {
        EditorIntelligencePreferences current = settingsState.EditorIntelligenceSettings?
            .Preferences ?? EditorIntelligencePreferences.Default;
        CheckBox parameterNames = new()
        {
            Content = "Show parameter-name hints",
            IsChecked = current.ShowParameterNameHints,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(parameterNames, "Show Roslyn parameter name inlay hints");
        CheckBox inferredTypes = new()
        {
            Content = "Show inferred types for var and implicit parameters",
            IsChecked = current.ShowInferredTypeHints,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(inferredTypes, "Show Roslyn inferred type inlay hints");
        CheckBox references = new()
        {
            Content = "Show Find references CodeLens",
            IsChecked = current.ShowReferenceCodeLens,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(references, "Show reference CodeLens actions");
        CheckBox implementations = new()
        {
            Content = "Show Find implementations CodeLens when applicable",
            IsChecked = current.ShowImplementationCodeLens,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(implementations, "Show implementation CodeLens actions");
        CheckBox tests = new()
        {
            Content = "Show Find tests CodeLens for types and methods",
            IsChecked = current.ShowTestCodeLens,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(tests, "Show associated test CodeLens actions");
        CheckBox run = new()
        {
            Content = "Show Run CodeLens for valid project entry points",
            IsChecked = current.ShowRunCodeLens,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(run, "Show project Run CodeLens actions");
        CheckBox debug = new()
        {
            Content = "Show Debug CodeLens when a debugger is available",
            IsChecked = current.ShowDebugCodeLens,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(debug, "Show project Debug CodeLens actions");
        CheckBox formatOnPaste = new()
        {
            Content = "Format pasted C# code with Roslyn",
            IsChecked = current.FormatOnPaste,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(formatOnPaste, "Format C# code on paste");
        CheckBox formatOnType = new()
        {
            Content = "Format after ;, }, or a new line",
            IsChecked = current.FormatOnType,
            IsEnabled = !settingsState.IsBusy,
        };
        AutomationProperties.SetName(formatOnType, "Format C# code on supported typing triggers");
        Button save = new()
        {
            Content = "Save editor settings",
            IsEnabled = !settingsState.IsBusy,
        };
        save.Classes.Add("primary");
        AutomationProperties.SetName(save, "Save editor intelligence settings");
        save.Click += async (_, _) => await store.SaveEditorIntelligenceSettingsAsync(new(
            parameterNames.IsChecked is true,
            inferredTypes.IsChecked is true,
            references.IsChecked is true,
            implementations.IsChecked is true,
            tests.IsChecked is true,
            formatOnPaste.IsChecked is true,
            formatOnType.IsChecked is true,
            run.IsChecked is true,
            debug.IsChecked is true), cancellationToken);

        return Page(
            "Editor",
            "Choose exact-buffer Roslyn hints, formatting, and lazy navigation for trusted C# editors.",
            new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = "Inlay hints", FontWeight = FontWeight.SemiBold },
                    parameterNames,
                    inferredTypes,
                    new TextBlock
                    {
                        Text = "Hints are computed only for the visible live buffer and never change source text.",
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock { Text = "Formatting", FontWeight = FontWeight.SemiBold },
                    formatOnPaste,
                    formatOnType,
                    new TextBlock
                    {
                        Text = "Automatic formatting is cancelled when the buffer changes, produces one undoable edit, and never saves the file.",
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock { Text = "CodeLens", FontWeight = FontWeight.SemiBold },
                    references,
                    implementations,
                    tests,
                    run,
                    debug,
                    new TextBlock
                    {
                        Text = "CodeLens actions resolve only when selected. Run and Debug appear only after a valid typed execution target is available.",
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                    save,
                    new TextBlock
                    {
                        Text = settingsState.EditorIntelligenceSettings?.Status ??
                               "Editor settings are loading.",
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = settingsState.Status ?? string.Empty,
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            });
    }

}
