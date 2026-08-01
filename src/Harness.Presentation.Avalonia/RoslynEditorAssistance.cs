using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Harness.BusinessLogic.CodeIntelligence;

namespace Harness.Presentation.Avalonia;

internal sealed class RoslynCompletionData(
    WorkbenchCodeCompletionItem item,
    Action<WorkbenchCodeCompletionItem, char?> complete) : ICompletionData
{
    public IImage? Image => null;
    public string Text => item.FilterText.Value;
    public object Content { get; } = ContentFor(item);
    public object Description { get; } = string.IsNullOrWhiteSpace(item.Description.Value)
        ? $"{item.Kind} · {item.DisplayText.Value}"
        : item.Description.Value;
    public double Priority => item.IsRecommended ? 1 : 0;
    internal IReadOnlyList<char> CommitCharacters => item.CommitCharacters;

    public void Complete(
        TextArea textArea,
        ISegment completionSegment,
        EventArgs insertionRequestEventArgs)
    {
        char? commitCharacter = insertionRequestEventArgs is TextInputEventArgs input &&
            input.Text is { Length: 1 }
            ? input.Text[0]
            : null;
        complete(item, commitCharacter);
    }

    internal void CompleteWithCharacter(char commitCharacter) => complete(item, commitCharacter);

    private static Control ContentFor(WorkbenchCodeCompletionItem value)
    {
        Grid row = new()
        {
            ColumnDefinitions = new("Auto,*,Auto"),
            ColumnSpacing = 8,
        };
        TextBlock kind = new()
        {
            Text = ShortKind(value.Kind),
            FontFamily = new("Cascadia Code,JetBrains Mono,Consolas,Menlo,monospace"),
            Opacity = 0.7,
        };
        TextBlock label = new()
        {
            Text = value.DisplayText.Value,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        TextBlock detail = new()
        {
            Text = value.Description.Value,
            Opacity = 0.65,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 220,
        };
        Grid.SetColumn(label, 1);
        Grid.SetColumn(detail, 2);
        row.Children.Add(kind);
        row.Children.Add(label);
        row.Children.Add(detail);
        AutomationProperties.SetName(
            row,
            $"{value.Kind} {value.DisplayText.Value} {value.Description.Value}".Trim());
        return row;
    }

    private static string ShortKind(WorkbenchCodeSymbolKind kind) => kind switch
    {
        WorkbenchCodeSymbolKind.Class => "C",
        WorkbenchCodeSymbolKind.Interface => "I",
        WorkbenchCodeSymbolKind.Structure => "S",
        WorkbenchCodeSymbolKind.Enumeration => "E",
        WorkbenchCodeSymbolKind.Method or WorkbenchCodeSymbolKind.ExtensionMethod => "M",
        WorkbenchCodeSymbolKind.Property => "P",
        WorkbenchCodeSymbolKind.Field => "F",
        WorkbenchCodeSymbolKind.Event => "V",
        WorkbenchCodeSymbolKind.Namespace => "N",
        WorkbenchCodeSymbolKind.Keyword => "K",
        WorkbenchCodeSymbolKind.Local or WorkbenchCodeSymbolKind.Parameter => "L",
        _ => "·",
    };
}

internal sealed class RoslynCompletionWindow : CompletionWindow
{
    internal RoslynCompletionWindow(TextArea textArea) : base(textArea)
    {
        TextInput += OnCompletionTextInput;
    }

    private void OnCompletionTextInput(object? sender, TextInputEventArgs args)
    {
        if (args.Text is not { Length: 1 } ||
            CompletionList.SelectedItem is not RoslynCompletionData selected ||
            !selected.CommitCharacters.Contains(args.Text[0]))
        {
            return;
        }

        args.Handled = true;
        Hide();
        selected.CompleteWithCharacter(args.Text[0]);
    }
}

internal sealed class RoslynOverloadProvider : IOverloadProvider
{
    private readonly IReadOnlyList<WorkbenchCodeSignatureItem> items;
    private readonly int selectedParameter;
    private int selectedIndex;

    internal RoslynOverloadProvider(WorkbenchCodeSignatureHelpView view)
    {
        items = view.Signatures;
        selectedParameter = view.SelectedParameter;
        selectedIndex = Math.Clamp(view.SelectedSignature, 0, Math.Max(0, items.Count - 1));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            selectedIndex = Math.Clamp(value, 0, Math.Max(0, items.Count - 1));
            PropertyChanged?.Invoke(this, new(nameof(SelectedIndex)));
            PropertyChanged?.Invoke(this, new(nameof(CurrentIndexText)));
            PropertyChanged?.Invoke(this, new(nameof(CurrentHeader)));
            PropertyChanged?.Invoke(this, new(nameof(CurrentContent)));
        }
    }

    public int Count => items.Count;
    public string CurrentIndexText => Count <= 1 ? string.Empty : $"{SelectedIndex + 1} of {Count}";
    public object CurrentHeader => items.Count == 0 ? string.Empty : Header(items[SelectedIndex]);
    public object CurrentContent => items.Count == 0
        ? string.Empty
        : items[SelectedIndex].Documentation.Value;

    private Control Header(WorkbenchCodeSignatureItem item)
    {
        StackPanel panel = new() { Spacing = 4, MaxWidth = 760 };
        TextBlock signature = new()
        {
            Text = item.Display.Value,
            FontFamily = new("Cascadia Code,JetBrains Mono,Consolas,Menlo,monospace"),
            TextWrapping = TextWrapping.Wrap,
        };
        panel.Children.Add(signature);
        if (selectedParameter >= 0 && selectedParameter < item.Parameters.Count)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"Parameter {selectedParameter + 1}: " +
                    item.Parameters[selectedParameter].Display.Value,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        AutomationProperties.SetName(panel,
            $"Signature {SelectedIndex + 1} of {Count}; parameter {selectedParameter + 1}; " +
            item.Display.Value);
        return panel;
    }
}
