using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Documents;

namespace Harness.Presentation.Avalonia;

/// <summary>
/// Owns the transient visual chrome around one real source editor. Document mutation,
/// access, and conflict policy remain in Business Logic and <see cref="SourceDocumentSession"/>.
/// </summary>
internal sealed class SourceEditorSurface : IDisposable
{
    private string codeHealth = "Code intelligence loading";
    private CodeDiagnosticRenderer renderer = null!;
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

    private SourceEditorSurface(
        Control control,
        TextEditor editor,
        TextBlock status,
        Button save,
        Button reload,
        Button close,
        Button completion,
        Button symbolInfo,
        Button definition,
        Button references,
        Button implementations)
    {
        Control = control;
        Editor = editor;
        Status = status;
        Save = save;
        Reload = reload;
        Close = close;
        Completion = completion;
        SymbolInfo = symbolInfo;
        Definition = definition;
        References = references;
        Implementations = implementations;
    }

    internal Control Control { get; }
    internal TextEditor Editor { get; }
    internal TextBlock Status { get; }
    internal Button Save { get; }
    internal Button Reload { get; }
    internal Button Close { get; }
    internal Button Completion { get; }
    internal Button SymbolInfo { get; }
    internal Button Definition { get; }
    internal Button References { get; }
    internal Button Implementations { get; }

    internal static SourceEditorSurface Create(WorkbenchDocumentView view)
    {
        TextEditor editor = CodeEditorView.Create(
            view.Content.Value,
            isReadOnly: view.Access is not WorkbenchDocumentAccess.Editable,
            wordWrap: false,
            showLineNumbers: true,
            path: view.Path.Value);
        editor.Classes.Add("source-editor");
        TextBlock status = new()
        {
            Text = view.AccessDescription,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Button save = Action("Save", $"Save {view.Path.Value}", "Save · Ctrl+S");
        save.IsEnabled = false;
        Button reload = Action("Reload", $"Reload {view.Path.Value}", "Reload from worktree");
        Button close = Action("Close", $"Close {view.Path.Value}", "Close · Ctrl+W");
        Button completion = Action("IntelliSense", $"Show IntelliSense for {view.Path.Value}",
            "Code completion · Ctrl+Space");
        Button symbolInfo = Action("Symbol info", $"Show symbol information for {view.Path.Value}",
            "Quick info · Ctrl+K");
        Button definition = Action("Definition", $"Go to definition in {view.Path.Value}",
            "Go to definition · F12");
        Button references = Action("Usages", $"Find usages in {view.Path.Value}",
            "Find usages · Shift+F12 or Alt+F7");
        Button implementations = Action("Implementations",
            $"Go to implementation in {view.Path.Value}",
            "Go to implementation · Ctrl+F12 or Ctrl+Alt+B");

        SourceEditorSurface surface = new(
            BuildRoot(editor, status),
            editor,
            status,
            save,
            reload,
            close,
            completion,
            symbolInfo,
            definition,
            references,
            implementations);
        surface.renderer = new(editor);
        surface.accessBadge.Child = surface.access;
        surface.accessBadge.Classes.Add("editor-access");
        surface.BuildHeader(save, reload, close);
        surface.BuildAssistanceBar(completion, symbolInfo, definition, references, implementations);
        surface.UpdateView(view);
        surface.UpdateMetrics();
        editor.TextArea.Caret.PositionChanged += (_, _) => surface.UpdateMetrics();
        editor.TextChanged += (_, _) => surface.UpdateMetrics();
        return surface;
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
        SymbolInfo.IsEnabled = semanticAssistance;
        Definition.IsEnabled = semanticAssistance;
        References.IsEnabled = semanticAssistance;
        Implementations.IsEnabled = semanticAssistance;
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
        string selection = selected == 0 ? string.Empty : $" · {selected:N0} selected";
        metrics.Text = $"Ln {Editor.TextArea.Caret.Line:N0}, Col {Editor.TextArea.Caret.Column:N0}" +
                       selection + $" · UTF-8 · {LineEndings(Editor.Text)} · {codeHealth}";
    }

    internal void UpdateCodeHealth(WorkbenchCodeDiagnosticView result)
    {
        renderer.SetDiagnostics(result.Diagnostics);
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
        renderer.SetDiagnostics([]);
        codeHealth = "Checking…";
        UpdateMetrics();
    }

    internal void SetCodeHealthNotApplicable()
    {
        renderer.SetDiagnostics([]);
        codeHealth = "Compiler check not applicable";
        UpdateMetrics();
    }

    public void Dispose() => renderer.Dispose();

    private static Button Action(string content, string name, string tip)
    {
        Button button = new() { Content = content };
        button.Classes.Add("editor-action");
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static Grid BuildRoot(TextEditor editor, TextBlock status)
    {
        Grid root = new() { RowDefinitions = new("Auto,Auto,*,Auto") };
        Grid.SetRow(editor, 2);
        root.Children.Add(editor);

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

        Border surface = new() { Child = commands };
        surface.Classes.Add("editor-assistance-toolbar");
        Grid.SetRow(surface, 1);
        ((Grid)Control).Children.Add(surface);
    }

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
}
