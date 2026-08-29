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
    private Control KeybindingsPage()
    {
        KeybindingSettingsSnapshot snapshot = settingsState.KeybindingSettings ??
                                               KeybindingSettingsSnapshot.Default;
        ComboBox inputMode = new()
        {
            ItemsSource = Enum.GetValues<EditorInputMode>(),
            SelectedItem = snapshot.InputMode,
            IsEnabled = !settingsState.IsBusy,
            MinWidth = 220,
        };
        AutomationProperties.SetName(inputMode, "Editor keyboard input mode");
        Dictionary<KeybindingCommand, TextBox> editors = [];
        TextBlock validation = new()
        {
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap,
        };
        Button save = new()
        {
            Content = "Save keybindings",
            IsEnabled = !settingsState.IsBusy,
            Classes = { "primary" },
        };
        AutomationProperties.SetName(save, "Save validated keybindings");

        StackPanel rows = new() { Spacing = 10 };
        foreach (KeybindingCommandBindings binding in snapshot.Bindings)
        {
            TextBox editor = new()
            {
                Text = binding.DisplayText,
                PlaceholderText = "Unbound",
                IsEnabled = !settingsState.IsBusy,
            };
            AutomationProperties.SetName(editor, $"Shortcut for {binding.Definition.Title}");
            editors.Add(binding.Definition.Command, editor);
            Grid row = new()
            {
                ColumnDefinitions = new("240,*"),
                ColumnSpacing = 12,
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 1,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = binding.Definition.Title,
                                FontWeight = FontWeight.SemiBold,
                            },
                            new TextBlock
                            {
                                Text = binding.Definition.Category,
                                Classes = { "muted" },
                                FontSize = 11,
                            },
                        },
                    },
                },
            };
            Grid.SetColumn(editor, 1);
            row.Children.Add(editor);
            rows.Children.Add(row);
        }

        KeybindingUpdateRequest Draft() => new(editors.Select(pair =>
                new KeybindingUpdateEntry(pair.Key, pair.Value.Text ?? string.Empty)).ToArray(),
            inputMode.SelectedItem is EditorInputMode selected
                ? selected
                : EditorInputMode.Standard);
        void ValidateDraft()
        {
            KeybindingValidationResult result = store.ValidateKeybindings(Draft());
            save.IsEnabled = result.IsValid && !settingsState.IsBusy;
            validation.Text = result.IsValid
                ? "No conflicts. Changes take effect immediately after saving."
                : string.Join('\n', result.Issues.Select(issue => $"• {issue.Message}").Distinct());
            validation.Classes.Set("warning", !result.IsValid);
        }
        foreach (TextBox editor in editors.Values)
        {
            editor.GetObservable(TextBox.TextProperty).Subscribe(_ => ValidateDraft());
        }
        inputMode.SelectionChanged += (_, _) => ValidateDraft();
        save.Click += async (_, _) =>
        {
            await store.SaveKeybindingsAsync(Draft(), cancellationToken);
            validation.Text = store.Current.Settings.Status ?? validation.Text;
        };

        Button reset = new() { Content = "Reset to defaults", IsEnabled = !settingsState.IsBusy };
        reset.Classes.Add("command");
        AutomationProperties.SetName(reset, "Reset all keybindings to defaults");
        reset.Click += async (_, _) =>
        {
            await store.ResetKeybindingsAsync(cancellationToken);
            RenderSelectedPage();
        };

        TextBox document = new()
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MinHeight = 150,
            MaxHeight = 260,
            PlaceholderText = "Versioned keybinding JSON appears here",
            IsEnabled = !settingsState.IsBusy,
            Text = keybindingDocumentText,
        };
        document.GetObservable(TextBox.TextProperty).Subscribe(value =>
            keybindingDocumentText = value ?? string.Empty);
        AutomationProperties.SetName(document, "Keybinding import and export document");
        Button export = new() { Content = "Export and copy JSON", IsEnabled = !settingsState.IsBusy };
        export.Classes.Add("command");
        AutomationProperties.SetName(export, "Export keybindings as safe JSON");
        export.Click += async (_, _) =>
        {
            string? text = await store.ExportKeybindingsAsync(cancellationToken);
            if (text is null) return;
            keybindingDocumentText = text;
            document.Text = text;
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
            }
        };
        Button import = new() { Content = "Validate and import JSON", IsEnabled = !settingsState.IsBusy };
        import.Classes.Add("command");
        AutomationProperties.SetName(import, "Validate and import keybinding JSON");
        import.Click += async (_, _) =>
        {
            await store.ImportKeybindingsAsync(document.Text ?? string.Empty, cancellationToken);
            validation.Text = store.Current.Settings.Status ?? validation.Text;
        };
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { save, reset },
        };
        StackPanel transfer = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { export, import },
        };

        ValidateDraft();
        return Page(
            "Keybindings",
            "Configure real workbench commands. Separate alternate gestures with a semicolon.",
            new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new Border
                    {
                        Classes = { "card" },
                        Child = new TextBlock
                        {
                            Text = "Reserved desktop, accessibility, and unmodified typing keys cannot be assigned. Conflicts block saving instead of choosing an arbitrary command.",
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                    Labeled("Editor input mode", inputMode,
                        "Vim starts each source editor in Normal mode. Escape or Ctrl+[ leaves Insert or Visual mode after IME composition ends. Application shortcuts remain active."),
                    rows,
                    validation,
                    actions,
                    new Separator(),
                    new TextBlock { Text = "Portable configuration", FontWeight = FontWeight.SemiBold },
                    new TextBlock
                    {
                        Text = "Import accepts only the bounded harness-keybindings-v1 JSON schema. It cannot name files, scripts, or executable actions.",
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                    document,
                    transfer,
                    new TextBlock
                    {
                        Text = settingsState.KeybindingSettings?.Status ??
                               "Keybinding settings are loading; safe defaults are shown.",
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
