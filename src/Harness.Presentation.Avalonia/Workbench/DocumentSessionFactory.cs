using Avalonia.Automation;
using Avalonia.Input;
using Dock.Model.Avalonia;
using Dock.Model.Core;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Editor;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed class DocumentSessionFactory
{
    private static readonly KeybindingCommand[] Commands =
    [
        KeybindingCommand.SaveDocument,
        KeybindingCommand.CloseDocument,
        KeybindingCommand.ShowCompletion,
        KeybindingCommand.ShowQuickInfo,
        KeybindingCommand.GoToDefinition,
        KeybindingCommand.FindReferences,
        KeybindingCommand.FindImplementations,
        KeybindingCommand.RenameSymbol,
        KeybindingCommand.FormatDocument,
        KeybindingCommand.FormatSelection,
        KeybindingCommand.OrganizeImports,
        KeybindingCommand.ShowQuickFixes,
    ];

    private readonly IFactory factory;
    private readonly Func<KeybindingSettingsSnapshot> keybindings;
    private readonly DocumentIntelligence intelligence;
    private readonly DocumentNavigation navigation;
    private readonly DocumentInteractions interactions;
    private readonly DocumentRename rename;
    private readonly DocumentTransformations transformations;
    private readonly Func<SourceDocumentSession, ValueTask<bool>> save;
    private readonly Func<SourceDocumentSession, bool, ValueTask<bool>> reload;
    private readonly Func<SourceDocumentSession, ValueTask> close;
    private readonly Func<SourceDocumentSession, bool> closeRequested;
    private readonly CancellationToken cancellationToken;

    internal DocumentSessionFactory(
        IFactory factory,
        Func<KeybindingSettingsSnapshot> keybindings,
        DocumentIntelligence intelligence,
        DocumentNavigation navigation,
        DocumentInteractions interactions,
        DocumentRename rename,
        DocumentTransformations transformations,
        Func<SourceDocumentSession, ValueTask<bool>> save,
        Func<SourceDocumentSession, bool, ValueTask<bool>> reload,
        Func<SourceDocumentSession, ValueTask> close,
        Func<SourceDocumentSession, bool> closeRequested,
        CancellationToken cancellationToken)
    {
        this.factory = factory;
        this.keybindings = keybindings;
        this.intelligence = intelligence;
        this.navigation = navigation;
        this.interactions = interactions;
        this.rename = rename;
        this.transformations = transformations;
        this.save = save;
        this.reload = reload;
        this.close = close;
        this.closeRequested = closeRequested;
        this.cancellationToken = cancellationToken;
    }

    internal SourceDocumentSession Create(string id, WorkbenchDocumentView view)
    {
        KeybindingSettingsSnapshot bindings = keybindings();
        SourceEditorSurface surface = SourceEditorSurface.Create(view, bindings);
        IWorkbenchEditorAdapter editor = surface.Editor;
        AutomationProperties.SetName(editor.Control,
            view.Access is WorkbenchDocumentAccess.Editable
                ? $"Editable source editor for {view.Path.Value}"
                : $"Read-only source editor for {view.Path.Value}");
        SourceDockDocument dock = new()
        {
            Id = id,
            Title = Title(view),
            Factory = factory,
            CanClose = true,
            CanFloat = true,
        };
        WorkbenchDockContent.Attach(dock, surface.Control);
        SourceDocumentSession document = new(dock, surface, view, bindings.InputMode);
        dock.CloseRequested = () => closeRequested(document);
        editor.TextChanged += (_, _) =>
        {
            document.CancelHover();
            editor.SetOccurrences([]);
            document.SynchronizeDirtyState();
            intelligence.ScheduleDiagnostics(document);
            intelligence.SchedulePresentation(document);
        };
        editor.CaretChanged += (_, _) => intelligence.ScheduleOccurrences(document);
        editor.CodeLensInvoked += async (_, args) =>
            await navigation.InvokeCodeLensAsync(document, args.Lens);
        surface.CodeLensInvoked += async (_, args) =>
            await navigation.InvokeCodeLensAsync(document, args.Lens);
        editor.ViewportChanged += (_, _) => intelligence.SchedulePresentation(
            document, includeStructure: false);
        editor.KeyDown += async (_, args) => await HandleKeyAsync(document, args);
        editor.TextEntered += async (_, args) =>
            await transformations.HandleTextEnteredAsync(document, args.Text);
        editor.TextPasted += async (_, args) =>
            await transformations.HandlePasteAsync(document, args.Range);
        editor.PointerPositionChanged += (_, args) =>
        {
            if (args.Position is { } position)
                _ = interactions.ShowQuickInfoOnHoverAsync(
                    document, position, document.BeginHover(cancellationToken));
        };
        editor.PointerExited += (_, _) => document.CancelHover();
        surface.Save.Click += async (_, _) => await save(document);
        surface.Reload.Click += async (_, _) => await reload(document, true);
        surface.Close.Click += async (_, _) => await close(document);
        surface.Completion.Click += async (_, _) => await interactions.ShowCompletionAsync(
            document, WorkbenchCodeCompletionTriggerKind.Invoke, null);
        surface.WorkspaceSymbols.Click += async (_, _) =>
            await interactions.ShowWorkspaceSymbolsAsync(document);
        surface.SymbolInfo.Click += async (_, _) => await interactions.ShowQuickInfoAsync(document);
        surface.Definition.Click += async (_, _) =>
            await navigation.NavigateAsync(document, SemanticNavigationKind.Definition);
        surface.References.Click += async (_, _) =>
            await navigation.NavigateAsync(document, SemanticNavigationKind.References);
        surface.Implementations.Click += async (_, _) =>
            await navigation.NavigateAsync(document, SemanticNavigationKind.Implementations);
        surface.InspectionRequested += async kind => await navigation.ShowInspectionAsync(document, kind);
        surface.FormatDocument.Click += async (_, _) => await transformations.TransformAsync(
            document, WorkbenchCodeDocumentTransformationKind.FormatDocument);
        surface.FormatSelection.Click += async (_, _) => await transformations.TransformAsync(
            document, WorkbenchCodeDocumentTransformationKind.FormatSelection);
        surface.FormatChangedSpans.Click += async (_, _) => await transformations.TransformAsync(
            document, WorkbenchCodeDocumentTransformationKind.FormatChangedSpans);
        surface.OrganizeImports.Click += async (_, _) => await transformations.TransformAsync(
            document, WorkbenchCodeDocumentTransformationKind.OrganizeImports);
        surface.RemoveUnusedImports.Click += async (_, _) => await transformations.TransformAsync(
            document, WorkbenchCodeDocumentTransformationKind.RemoveUnusedImports);
        surface.QuickFix.Click += async (_, _) => await transformations.ShowQuickFixesAsync(document);
        surface.NavigationRequested += position =>
        {
            editor.SetCaretPosition(position);
            editor.ScrollTo(position);
            editor.Focus();
        };
        document.SynchronizeDirtyState();
        intelligence.ScheduleDiagnostics(document, true);
        intelligence.SchedulePresentation(document, true);
        return document;
    }

    private async ValueTask HandleKeyAsync(SourceDocumentSession document, KeyEventArgs args)
    {
        KeybindingCommand? command = KeybindingInput.Match(args, keybindings(), Commands);
        if (command is not null)
        {
            args.Handled = true;
            await InvokeAsync(document, command.Value);
        }
        else if (document.Vim.ShouldHandle(args))
        {
            args.Handled = true;
            _ = document.Vim.Handle(args);
        }
    }

    internal async ValueTask InvokeAsync(SourceDocumentSession document, KeybindingCommand command)
    {
        switch (command)
        {
            case KeybindingCommand.SaveDocument: await save(document); break;
            case KeybindingCommand.CloseDocument: await close(document); break;
            case KeybindingCommand.ShowCompletion:
                await interactions.ShowCompletionAsync(
                    document, WorkbenchCodeCompletionTriggerKind.Invoke, null); break;
            case KeybindingCommand.ShowQuickInfo: await interactions.ShowQuickInfoAsync(document); break;
            case KeybindingCommand.GoToDefinition:
                await navigation.NavigateAsync(document, SemanticNavigationKind.Definition); break;
            case KeybindingCommand.FindReferences:
                await navigation.NavigateAsync(document, SemanticNavigationKind.References); break;
            case KeybindingCommand.FindImplementations:
                await navigation.NavigateAsync(document, SemanticNavigationKind.Implementations); break;
            case KeybindingCommand.RenameSymbol: await rename.RenameAsync(document); break;
            case KeybindingCommand.FormatDocument:
                await transformations.TransformAsync(
                    document, WorkbenchCodeDocumentTransformationKind.FormatDocument); break;
            case KeybindingCommand.FormatSelection:
                await transformations.TransformAsync(
                    document, WorkbenchCodeDocumentTransformationKind.FormatSelection); break;
            case KeybindingCommand.OrganizeImports:
                await transformations.TransformAsync(
                    document, WorkbenchCodeDocumentTransformationKind.OrganizeImports); break;
            case KeybindingCommand.ShowQuickFixes:
                await transformations.ShowQuickFixesAsync(document); break;
            default: throw new ArgumentOutOfRangeException(nameof(command));
        }
    }

    private static string Title(WorkbenchDocumentView view)
    {
        string title = Path.GetFileName(view.Path.Value);
        if (view.IsTruncated) return $"{title} · truncated";
        return view.Branch is null ? title : $"{title} · {view.Branch.Value}";
    }
}
