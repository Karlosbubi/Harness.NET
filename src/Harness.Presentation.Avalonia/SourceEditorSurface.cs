using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Editor;

namespace Harness.Presentation.Avalonia;

/// <summary>
/// Owns the transient visual chrome around one real source editor. Document mutation,
/// access, and conflict policy remain in Business Logic and <see cref="SourceDocumentSession"/>.
/// </summary>
internal sealed class SourceEditorSurface : IDisposable
{
    private string codeHealth = "Code intelligence loading";
    private string inputMode = string.Empty;
    private readonly TextBlock path = new()
    {
        FontFamily = new FontFamily("Cascadia Code,JetBrains Mono,Consolas,Menlo,monospace"),
        FontSize = 12,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock context = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock access = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Border accessBadge = new();
    private readonly TextBlock metrics = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Right,
    };
    private readonly WrapPanel breadcrumbs = new()
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly ListBox outlineItems = new() { MaxHeight = 420, MinWidth = 360 };
    private IReadOnlyList<WorkbenchCodeOutlineItem> outline = [];

    private SourceEditorSurface(
        Control control,
        IWorkbenchEditorAdapter editor,
        TextBlock status,
        Button save,
        Button reload,
        Button close,
        Button outline,
        Button workspaceSymbols,
        Button completion,
        Button symbolInfo,
        Button definition,
        Button references,
        Button implementations,
        Button inspect,
        Button formatDocument,
        Button formatSelection,
        Button formatChangedSpans,
        Button organizeImports,
        Button removeUnusedImports,
        Button quickFix)
    {
        Control = control;
        Editor = editor;
        Status = status;
        Save = save;
        Reload = reload;
        Close = close;
        Outline = outline;
        WorkspaceSymbols = workspaceSymbols;
        Completion = completion;
        SymbolInfo = symbolInfo;
        Definition = definition;
        References = references;
        Implementations = implementations;
        Inspect = inspect;
        FormatDocument = formatDocument;
        FormatSelection = formatSelection;
        FormatChangedSpans = formatChangedSpans;
        OrganizeImports = organizeImports;
        RemoveUnusedImports = removeUnusedImports;
        QuickFix = quickFix;
    }

    internal Control Control { get; }
    internal IWorkbenchEditorAdapter Editor { get; }
    internal TextBlock Status { get; }
    internal Button Save { get; }
    internal Button Reload { get; }
    internal Button Close { get; }
    internal Button Outline { get; }
    internal Button WorkspaceSymbols { get; }
    internal Button Completion { get; }
    internal Button SymbolInfo { get; }
    internal Button Definition { get; }
    internal Button References { get; }
    internal Button Implementations { get; }
    internal Button Inspect { get; }
    internal Button FormatDocument { get; }
    internal Button FormatSelection { get; }
    internal Button FormatChangedSpans { get; }
    internal Button OrganizeImports { get; }
    internal Button RemoveUnusedImports { get; }
    internal Button QuickFix { get; }
    internal event Action<WorkbenchCodePosition>? NavigationRequested;
    internal event Action<WorkbenchCodeInspectionKind>? InspectionRequested;

    internal static SourceEditorSurface Create(
        WorkbenchDocumentView view,
        KeybindingSettingsSnapshot keybindings)
    {
        IWorkbenchEditorAdapter editor = new AvaloniaEditWorkbenchEditorAdapter(view);
        editor.Control.Classes.Add("source-editor");
        TextBlock status = new()
        {
            Text = view.AccessDescription,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Button save = Action("Save", $"Save {view.Path.Value}", "Save document");
        save.IsEnabled = false;
        Button reload = Action("Reload", $"Reload {view.Path.Value}", "Reload from worktree");
        Button close = Action("Close", $"Close {view.Path.Value}", "Close document");
        Button outline = Action("Outline", $"Show document outline for {view.Path.Value}",
            "Document outline");
        Button workspaceSymbols = Action("Symbols", "Search workspace symbols",
            "Search types and members across the workspace");
        Button completion = Action("IntelliSense", $"Show IntelliSense for {view.Path.Value}",
            "Code completion");
        Button symbolInfo = Action("Symbol info", $"Show symbol information for {view.Path.Value}",
            "Quick info");
        Button definition = Action("Definition", $"Go to definition in {view.Path.Value}",
            "Go to definition");
        Button references = Action("Usages", $"Find usages in {view.Path.Value}",
            "Find usages");
        Button implementations = Action("Implementations",
            $"Go to implementation in {view.Path.Value}",
            "Go to implementation");
        Button syntaxTree = Action("Syntax tree", $"Inspect syntax tree for {view.Path.Value}",
            "Read-only exact-buffer Roslyn syntax tree");
        Button symbolDetails = Action("Symbol details", $"Inspect symbol in {view.Path.Value}",
            "Read-only semantic symbol details at the caret");
        Button generatedSource = Action("Generated source",
            $"Inspect generated source for {view.Path.Value}",
            "Read-only source-generator output for the exact project");
        Button intermediateLanguage = Action("IL", $"Inspect IL for {view.Path.Value}",
            "Emit and disassemble the exact method at the caret");
        Button inspect = Action("Inspect", $"Inspect compiled context for {view.Path.Value}",
            "Syntax tree, symbol, generated source, and Intermediate Language");
        inspect.Flyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 4,
                Margin = new Thickness(4),
                Children = { syntaxTree, symbolDetails, generatedSource, intermediateLanguage },
            },
        };
        Button formatDocument = Action("Format document",
            $"Format document {view.Path.Value}", "Format document");
        Button formatSelection = Action("Format selection",
            $"Format selection in {view.Path.Value}", "Format selected code");
        Button formatChangedSpans = Action("Format changed code",
            $"Format changed code in {view.Path.Value}",
            "Format only spans changed since the file was loaded");
        Button organizeImports = Action("Organize imports",
            $"Organize imports in {view.Path.Value}", "Sort and group using directives");
        Button removeUnusedImports = Action("Remove unused imports",
            $"Remove unused imports from {view.Path.Value}",
            "Remove compiler-proven unused using directives");
        Button quickFix = Action("Quick fix…", $"Show quick fixes for {view.Path.Value}",
            "Show Roslyn fixes at the caret");
        Button transform = Action("Transform", $"Transform {view.Path.Value}",
            "Deterministic Roslyn formatting and imports");
        transform.Flyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 4,
                Margin = new Thickness(4),
                Children =
                {
                    formatDocument,
                    formatSelection,
                    formatChangedSpans,
                    organizeImports,
                    removeUnusedImports,
                },
            },
        };

        SourceEditorSurface surface = new(
            BuildRoot(editor, status),
            editor,
            status,
            save,
            reload,
            close,
            outline,
            workspaceSymbols,
            completion,
            symbolInfo,
            definition,
            references,
            implementations,
            inspect,
            formatDocument,
            formatSelection,
            formatChangedSpans,
            organizeImports,
            removeUnusedImports,
            quickFix);
        surface.accessBadge.Child = surface.access;
        surface.accessBadge.Classes.Add("editor-access");
        surface.BuildHeader(save, reload, close);
        surface.BuildAssistanceBar(outline, workspaceSymbols, completion, symbolInfo, definition,
            references, implementations, inspect, quickFix, transform);
        syntaxTree.Click += (_, _) =>
            surface.InspectionRequested?.Invoke(WorkbenchCodeInspectionKind.SyntaxTree);
        symbolDetails.Click += (_, _) =>
            surface.InspectionRequested?.Invoke(WorkbenchCodeInspectionKind.Symbol);
        generatedSource.Click += (_, _) =>
            surface.InspectionRequested?.Invoke(WorkbenchCodeInspectionKind.GeneratedSource);
        intermediateLanguage.Click += (_, _) =>
            surface.InspectionRequested?.Invoke(WorkbenchCodeInspectionKind.IntermediateLanguage);
        surface.ConfigureOutline(outline);
        surface.ApplyKeybindings(keybindings);
        surface.UpdateView(view);
        surface.UpdateMetrics();
        editor.CaretChanged += (_, _) => surface.UpdateMetrics();
        editor.TextChanged += (_, _) => surface.UpdateMetrics();
        return surface;
    }

    internal void ApplyKeybindings(KeybindingSettingsSnapshot settings)
    {
        SetShortcutTip(Save, "Save", settings, KeybindingCommand.SaveDocument);
        SetShortcutTip(Close, "Close", settings, KeybindingCommand.CloseDocument);
        SetShortcutTip(Completion, "Code completion", settings, KeybindingCommand.ShowCompletion);
        SetShortcutTip(SymbolInfo, "Quick info", settings, KeybindingCommand.ShowQuickInfo);
        SetShortcutTip(Definition, "Go to definition", settings, KeybindingCommand.GoToDefinition);
        SetShortcutTip(References, "Find usages", settings, KeybindingCommand.FindReferences);
        SetShortcutTip(Implementations, "Go to implementation", settings,
            KeybindingCommand.FindImplementations);
        SetShortcutTip(FormatDocument, "Format document", settings,
            KeybindingCommand.FormatDocument);
        SetShortcutTip(FormatSelection, "Format selected code", settings,
            KeybindingCommand.FormatSelection);
        SetShortcutTip(OrganizeImports, "Sort and group using directives", settings,
            KeybindingCommand.OrganizeImports);
        SetShortcutTip(QuickFix, "Show Roslyn fixes at the caret", settings,
            KeybindingCommand.ShowQuickFixes);
    }

    private static void SetShortcutTip(
        Control control,
        string description,
        KeybindingSettingsSnapshot settings,
        KeybindingCommand command)
    {
        string shortcut = settings.DisplayFor(command).Replace("; ", " or ",
            StringComparison.Ordinal);
        ToolTip.SetTip(control, shortcut.Length == 0
            ? $"{description} · unbound"
            : $"{description} · {shortcut}");
    }

    internal void UpdateView(WorkbenchDocumentView view)
    {
        path.Text = view.Path.Value.Replace("/", " › ", StringComparison.Ordinal);
        context.Text = view.Branch is null ? "Original workspace" : view.Branch.Value;
        access.Text = view.IsTruncated
            ? "TRUNCATED"
            : view.Access is WorkbenchDocumentAccess.Editable ? "EDITABLE" : "READ ONLY";
        accessBadge.Classes.Remove("editable");
        accessBadge.Classes.Remove("read-only");
        accessBadge.Classes.Remove("truncated");
        accessBadge.Classes.Add(view.IsTruncated
            ? "truncated"
            : view.Access is WorkbenchDocumentAccess.Editable ? "editable" : "read-only");
        bool semanticAssistance = !view.IsTruncated && Path.GetExtension(view.Path.Value)
            .Equals(".cs", StringComparison.OrdinalIgnoreCase);
        Completion.IsEnabled = semanticAssistance;
        WorkspaceSymbols.IsEnabled = semanticAssistance;
        SymbolInfo.IsEnabled = semanticAssistance;
        Definition.IsEnabled = semanticAssistance;
        References.IsEnabled = semanticAssistance;
        Implementations.IsEnabled = semanticAssistance;
        Inspect.IsEnabled = semanticAssistance;
        FormatDocument.IsEnabled = semanticAssistance &&
            view.Access is WorkbenchDocumentAccess.Editable;
        OrganizeImports.IsEnabled = FormatDocument.IsEnabled;
        RemoveUnusedImports.IsEnabled = FormatDocument.IsEnabled;
        QuickFix.IsEnabled = semanticAssistance && view.Access is WorkbenchDocumentAccess.Editable;
        FormatSelection.IsEnabled = FormatDocument.IsEnabled && Editor.SelectionLength > 0;
        FormatChangedSpans.IsEnabled = FormatDocument.IsEnabled;
        AutomationProperties.SetName(path, $"Repository path {view.Path.Value}");
        AutomationProperties.SetName(Status, $"Editing status for {view.Path.Value}");
        AutomationProperties.SetName(metrics, $"Caret and format for {view.Path.Value}");
        AutomationProperties.SetName(
            accessBadge,
            view.IsTruncated
                ? $"Truncated read-only source {view.Path.Value}"
                : view.Access is WorkbenchDocumentAccess.Editable
                    ? $"Editable source {view.Path.Value}"
                    : $"Read-only original workspace source {view.Path.Value}");
        UpdateMetrics();
    }

    internal void UpdateMetrics()
    {
        int selected = Editor.SelectionLength;
        FormatSelection.IsEnabled = FormatDocument.IsEnabled && selected > 0;
        string selection = selected == 0 ? string.Empty : $" · {selected:N0} selected";
        WorkbenchCodePosition caret = Editor.CaretPosition;
        metrics.Text = (inputMode.Length == 0 ? string.Empty : $"{inputMode} · ") +
                       $"Ln {caret.Line + 1:N0}, Col {caret.Character + 1:N0}" +
                       selection + $" · UTF-8 · {LineEndings(Editor.Text)} · {codeHealth}";
        UpdateBreadcrumbs(caret);
    }

    internal void SetInputModeStatus(string status)
    {
        inputMode = status;
        UpdateMetrics();
    }

    internal void UpdateDocumentPresentation(WorkbenchCodeDocumentPresentationView presentation)
    {
        Editor.SetDocumentPresentation(presentation);
        if (presentation.FoldingRanges.Count == 0 && presentation.Outline.Count == 0)
        {
            return;
        }
        outline = presentation.Outline;
        outlineItems.ItemsSource = outline.Select(item => new OutlineChoice(item)).ToArray();
        Outline.IsEnabled = outline.Count > 0;
        UpdateBreadcrumbs(Editor.CaretPosition);
    }

    internal void UpdateCodeHealth(WorkbenchCodeDiagnosticView result)
    {
        Editor.SetDiagnostics(result.Diagnostics);
        int errors = result.Diagnostics.Count(item =>
            item.Severity is WorkbenchCodeDiagnosticSeverity.Error);
        int warnings = result.Diagnostics.Count(item =>
            item.Severity is WorkbenchCodeDiagnosticSeverity.Warning);
        codeHealth = result.State switch
        {
            WorkbenchCodeResultState.Ready or WorkbenchCodeResultState.Degraded when errors > 0 =>
                $"{errors:N0} error(s), {warnings:N0} warning(s)",
            WorkbenchCodeResultState.Ready or WorkbenchCodeResultState.Degraded when warnings > 0 =>
                $"{warnings:N0} warning(s)",
            WorkbenchCodeResultState.Ready => "No problems",
            WorkbenchCodeResultState.Degraded => "Code intelligence degraded",
            WorkbenchCodeResultState.Loading => "Code intelligence loading",
            WorkbenchCodeResultState.Cancelled => "Check cancelled",
            WorkbenchCodeResultState.Failed => "Code intelligence failed",
            WorkbenchCodeResultState.Stale => "Checking newer buffer",
            _ => "Code intelligence unavailable",
        };
        UpdateMetrics();
    }

    internal void BeginCodeHealthUpdate()
    {
        Editor.SetDiagnostics([]);
        codeHealth = "Checking…";
        UpdateMetrics();
    }

    internal void SetCodeHealthNotApplicable()
    {
        Editor.SetDiagnostics([]);
        codeHealth = "Compiler check not applicable";
        UpdateMetrics();
    }

    public void Dispose() => Editor.Dispose();

    private static Button Action(string content, string name, string tip)
    {
        Button button = new() { Content = content };
        button.Classes.Add("editor-action");
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static Grid BuildRoot(IWorkbenchEditorAdapter editor, TextBlock status)
    {
        Grid root = new() { RowDefinitions = new("Auto,Auto,*,Auto") };
        Grid.SetRow(editor.Control, 2);
        root.Children.Add(editor.Control);

        Grid footer = new()
        {
            ColumnDefinitions = new("*,Auto"),
            ColumnSpacing = 12,
            Children = { status },
        };
        footer.Classes.Add("editor-statusbar");
        Border footerBorder = new() { Child = footer };
        footerBorder.Classes.Add("editor-status-surface");
        Grid.SetRow(footerBorder, 3);
        root.Children.Add(footerBorder);
        return root;
    }

    private void BuildAssistanceBar(params Button[] actions)
    {
        WrapPanel commands = new() { Orientation = Orientation.Horizontal };
        foreach (Button action in actions)
        {
            commands.Children.Add(action);
        }

        Grid content = new()
        {
            ColumnDefinitions = new("*,Auto"),
            ColumnSpacing = 8,
            Children = { breadcrumbs },
        };
        Grid.SetColumn(commands, 1);
        content.Children.Add(commands);
        Border surface = new() { Child = content };
        surface.Classes.Add("editor-assistance-toolbar");
        Grid.SetRow(surface, 1);
        ((Grid)Control).Children.Add(surface);
    }

    private void ConfigureOutline(Button button)
    {
        AutomationProperties.SetName(outlineItems, "Document outline symbols");
        Flyout flyout = new()
        {
            Content = new Border
            {
                Padding = new Thickness(8),
                Child = outlineItems,
            },
        };
        button.Flyout = flyout;
        outlineItems.SelectionChanged += (_, _) =>
        {
            if (outlineItems.SelectedItem is not OutlineChoice choice)
                return;
            NavigationRequested?.Invoke(choice.Item.SelectionRange.Start);
            flyout.Hide();
            outlineItems.SelectedItem = null;
        };
    }

    private void UpdateBreadcrumbs(WorkbenchCodePosition caret)
    {
        breadcrumbs.Children.Clear();
        WorkbenchCodeOutlineItem[] path = outline
            .Where(item => Contains(item.Range, caret))
            .OrderBy(item => item.Depth)
            .ThenByDescending(item => SpanSize(item.Range))
            .ToArray();
        foreach (WorkbenchCodeOutlineItem item in path)
        {
            Button button = new()
            {
                Content = item.Display.Value,
                Padding = new Thickness(5, 2),
                MinHeight = 0,
            };
            button.Classes.Add("editor-breadcrumb");
            AutomationProperties.SetName(button, $"Go to {item.Display.Value}");
            button.Click += (_, _) => NavigationRequested?.Invoke(item.SelectionRange.Start);
            breadcrumbs.Children.Add(button);
        }
    }

    private static bool Contains(WorkbenchCodeRange range, WorkbenchCodePosition position) =>
        Compare(position, range.Start) >= 0 && Compare(position, range.End) <= 0;

    private static int Compare(WorkbenchCodePosition left, WorkbenchCodePosition right) =>
        left.Line != right.Line
            ? left.Line.CompareTo(right.Line)
            : left.Character.CompareTo(right.Character);

    private static long SpanSize(WorkbenchCodeRange range) =>
        ((long)range.End.Line - range.Start.Line) * 1_000_000L +
        range.End.Character - range.Start.Character;

    private void BuildHeader(Button save, Button reload, Button close)
    {
        Grid header = new()
        {
            ColumnDefinitions = new("*,Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 6,
            Children = { path },
        };
        context.Classes.Add("editor-context");
        Grid.SetColumn(context, 1);
        header.Children.Add(context);
        Grid.SetColumn(accessBadge, 2);
        header.Children.Add(accessBadge);
        Grid.SetColumn(save, 3);
        header.Children.Add(save);
        Grid.SetColumn(reload, 4);
        header.Children.Add(reload);
        Grid.SetColumn(close, 5);
        header.Children.Add(close);
        Border headerBorder = new() { Child = header };
        headerBorder.Classes.Add("editor-toolbar");
        Grid root = (Grid)Control;
        root.Children.Insert(0, headerBorder);

        Grid footer = (Grid)((Border)root.Children[^1]).Child!;
        Grid.SetColumn(metrics, 1);
        footer.Children.Add(metrics);
    }

    private static string LineEndings(string text)
    {
        bool crlf = text.Contains("\r\n", StringComparison.Ordinal);
        bool lf = text.Replace("\r\n", string.Empty, StringComparison.Ordinal)
            .Contains('\n');
        if (crlf && lf)
        {
            return "Mixed endings";
        }

        if (crlf)
        {
            return "CRLF";
        }

        return lf ? "LF" : "No line break";
    }

    private sealed record OutlineChoice(WorkbenchCodeOutlineItem Item)
    {
        public override string ToString() =>
            $"{new string(' ', Math.Max(0, Item.Depth) * 2)}{Item.Display.Value}";
    }
}
