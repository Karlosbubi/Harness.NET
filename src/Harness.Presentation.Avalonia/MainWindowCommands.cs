using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Appearance;
using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.CodeIntelligence;
using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Execution;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Inspection;
using Harness.BusinessLogic.Layouts;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.ProjectSecrets;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Harness.Presentation.Avalonia.Workbench;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia;

internal sealed partial class MainWindow : Window
{
    private async Task ShowCommandPaletteAsync()
    {
        CommandPaletteDialog palette = new(BuildCommands());
        await palette.ShowDialog(this);
    }

    /// <summary>
    /// Describes the commands the shell can actually run right now. A command that needs
    /// an active or trusted workspace is listed with the reason instead of being hidden.
    /// </summary>
    internal IReadOnlyList<PaletteCommand> BuildCommands()
    {
        AvaloniaShellState state = store.Current;
        KeybindingSettingsSnapshot bindings = state.Settings.KeybindingSettings ??
                                              KeybindingSettingsSnapshot.Default;
        WorkspaceView? active = state.Workspaces.Registered.FirstOrDefault(item => item.IsActive);
        string? needsWorkspace = active is null ? "Open a workspace first" : null;
        string? needsTrust = active is null
            ? "Open a workspace first"
            : active.IsTrusted ? null : "Trust the workspace first";
        string? needsGoal = state.Goals.SelectedGoal is null
            ? "Create or continue a goal first"
            : needsTrust;

        List<PaletteCommand> commands =
        [
            new("workspace.open", "Workspace", "Open workspace…",
                () => new(ShowWorkspaceDialogAsync(true)),
                bindings.DisplayFor(KeybindingCommand.OpenWorkspace),
                Binding: KeybindingCommand.OpenWorkspace),
            new("workspace.manage", "Workspace", "Manage workspaces…",
                () => new(ShowWorkspaceDialogAsync(false)),
                bindings.DisplayFor(KeybindingCommand.ManageWorkspaces),
                Binding: KeybindingCommand.ManageWorkspaces),
            new("workspace.user-secrets", "Workspace", "Manage project User Secrets…",
                () => new(ShowProjectUserSecretsAsync()),
                bindings.DisplayFor(KeybindingCommand.ManageProjectUserSecrets),
                UnavailableReason: needsTrust,
                MatchText: "Workspace Project User Secrets credentials development dotnet",
                Binding: KeybindingCommand.ManageProjectUserSecrets),
            new("workspace.quick.open", "Workspace", "Go to file…",
                () => new(ShowQuickOpenAsync()),
                bindings.DisplayFor(KeybindingCommand.QuickOpen),
                UnavailableReason: needsTrust,
                Binding: KeybindingCommand.QuickOpen),
            new("goal.context", "Goal", "Inspect semantic context…",
                () => new(ShowSemanticContextAsync()),
                bindings.DisplayFor(KeybindingCommand.InspectSemanticContext),
                UnavailableReason: state.Goals.SelectedGoal is null
                    ? "Create or continue a goal first"
                    : needsTrust,
                Binding: KeybindingCommand.InspectSemanticContext),
            new("framework.manage", "Framework", "Effective framework…",
                () => new(ShowDialogAsync(new FrameworkDialog(store, cancellationToken))),
                bindings.DisplayFor(KeybindingCommand.ManageFramework),
                UnavailableReason: needsWorkspace,
                Binding: KeybindingCommand.ManageFramework),
            new("settings.open", "Application", "Settings…",
                () => new(ShowSettingsAsync()), bindings.DisplayFor(KeybindingCommand.OpenSettings),
                Binding: KeybindingCommand.OpenSettings),
            new("operations.manage", "Application", "Operations and backup…",
                () => new(ShowDialogAsync(new OperationsDialog(store, cancellationToken))),
                bindings.DisplayFor(KeybindingCommand.ManageOperations),
                Binding: KeybindingCommand.ManageOperations),
            new("provider.refresh", "Providers", "Refresh provider health",
                async () => await store.RefreshProviderAsync(cancellationToken),
                bindings.DisplayFor(KeybindingCommand.RefreshProviderHealth),
                Binding: KeybindingCommand.RefreshProviderHealth),
            new("themes.reload", "Appearance", "Reload user themes",
                async () => await store.RefreshThemesAsync(cancellationToken),
                bindings.DisplayFor(KeybindingCommand.ReloadUserThemes),
                Binding: KeybindingCommand.ReloadUserThemes),
        ];

        if (workbench is { } host)
        {
            commands.AddRange(
            [
                new("tool.files", "Panels", "Show Files panel",
                    () => { host.ShowFiles(); return ValueTask.CompletedTask; },
                    bindings.DisplayFor(KeybindingCommand.ShowFiles),
                    Binding: KeybindingCommand.ShowFiles),
                new("tool.conversation", "Panels", "Show Chat panel",
                    () => { ShowConversation(); return ValueTask.CompletedTask; },
                    bindings.DisplayFor(KeybindingCommand.ShowChat),
                    MatchText: "Panels Show Chat Conversation goal agent message",
                    Binding: KeybindingCommand.ShowChat),
                new("tool.git", "Panels", "Show Git panel",
                    () => { host.ShowGit(); return ValueTask.CompletedTask; },
                    bindings.DisplayFor(KeybindingCommand.ShowGit),
                    Binding: KeybindingCommand.ShowGit),
                new("tool.output", "Panels", "Show Run output panel",
                    () => { host.ShowRunOutput(); return ValueTask.CompletedTask; },
                    bindings.DisplayFor(KeybindingCommand.ShowRunOutput),
                    Binding: KeybindingCommand.ShowRunOutput),
                new("tool.problems", "Panels", "Show Problems panel",
                    () => { host.ShowProblems(); return ValueTask.CompletedTask; },
                    bindings.DisplayFor(KeybindingCommand.ShowProblems),
                    Binding: KeybindingCommand.ShowProblems),
                new("git.diff", "Git", "Open working-tree diff",
                    async () => await host.OpenDiffAsync(),
                    bindings.DisplayFor(KeybindingCommand.OpenWorkingTreeDiff),
                    UnavailableReason: needsTrust,
                    Binding: KeybindingCommand.OpenWorkingTreeDiff),
                EditorCommand("editor.save", "Save document", KeybindingCommand.SaveDocument,
                    "Open an editable document first"),
                EditorCommand("editor.close", "Close document", KeybindingCommand.CloseDocument,
                    "Open a source document first"),
                EditorCommand("editor.completion", "Show completion", KeybindingCommand.ShowCompletion,
                    "Open a C# document first"),
                EditorCommand("editor.quick.info", "Show quick info", KeybindingCommand.ShowQuickInfo,
                    "Open a C# document first"),
                EditorCommand("editor.definition", "Go to definition", KeybindingCommand.GoToDefinition,
                    "Open a C# document first"),
                EditorCommand("editor.references", "Find references", KeybindingCommand.FindReferences,
                    "Open a C# document first"),
                EditorCommand("editor.implementations", "Find implementations",
                    KeybindingCommand.FindImplementations, "Open a C# document first"),
                EditorCommand("editor.rename", "Rename symbol", KeybindingCommand.RenameSymbol,
                    "Open an editable C# document first"),
                new("editor.format.document", "Editor", "Format document",
                    async () => await host.InvokeActiveEditorCommandAsync(
                        KeybindingCommand.FormatDocument),
                    bindings.DisplayFor(KeybindingCommand.FormatDocument),
                    UnavailableReason: host.CanInvokeActiveEditorCommand(KeybindingCommand.FormatDocument)
                            ? null : "Open an editable C# document first",
                    Binding: KeybindingCommand.FormatDocument),
                new("editor.format.selection", "Editor", "Format selection",
                    async () => await host.InvokeActiveEditorCommandAsync(
                        KeybindingCommand.FormatSelection),
                    bindings.DisplayFor(KeybindingCommand.FormatSelection),
                    UnavailableReason: host.CanInvokeActiveEditorCommand(KeybindingCommand.FormatSelection)
                            ? null : "Select code in an editable C# document first",
                    Binding: KeybindingCommand.FormatSelection),
                new("editor.format.changed", "Editor", "Format changed code",
                    async () => await host.TransformActiveDocumentAsync(
                        WorkbenchCodeDocumentTransformationKind.FormatChangedSpans),
                    bindings.DisplayFor(KeybindingCommand.FormatChangedCode),
                    UnavailableReason: host.CanTransformActiveDocument(
                        WorkbenchCodeDocumentTransformationKind.FormatChangedSpans)
                            ? null : "Open an editable C# document first",
                    Binding: KeybindingCommand.FormatChangedCode),
                new("editor.organize.imports", "Editor", "Organize imports",
                    async () => await host.InvokeActiveEditorCommandAsync(
                        KeybindingCommand.OrganizeImports),
                    bindings.DisplayFor(KeybindingCommand.OrganizeImports),
                    UnavailableReason: host.CanInvokeActiveEditorCommand(KeybindingCommand.OrganizeImports)
                            ? null : "Open an editable C# document first",
                    Binding: KeybindingCommand.OrganizeImports),
                new("editor.remove.unused.imports", "Editor", "Remove unused imports",
                    async () => await host.TransformActiveDocumentAsync(
                        WorkbenchCodeDocumentTransformationKind.RemoveUnusedImports),
                    bindings.DisplayFor(KeybindingCommand.RemoveUnusedImports),
                    UnavailableReason: host.CanTransformActiveDocument(
                        WorkbenchCodeDocumentTransformationKind.RemoveUnusedImports)
                            ? null : "Open an editable C# document first",
                    Binding: KeybindingCommand.RemoveUnusedImports),
                new("editor.quick.fix", "Editor", "Show quick fixes",
                    async () => await host.InvokeActiveEditorCommandAsync(
                        KeybindingCommand.ShowQuickFixes),
                    bindings.DisplayFor(KeybindingCommand.ShowQuickFixes),
                    UnavailableReason: host.CanInvokeActiveEditorCommand(KeybindingCommand.ShowQuickFixes)
                            ? null : "Open an editable C# document first",
                    Binding: KeybindingCommand.ShowQuickFixes),
                new("layout.save", "Layout", "Save workbench layout",
                    async () => await host.SaveLayoutAsync(),
                    bindings.DisplayFor(KeybindingCommand.SaveWorkbenchLayout),
                    Binding: KeybindingCommand.SaveWorkbenchLayout),
                new("layout.reset", "Layout", "Reset workbench layout",
                    async () => await host.ResetLayoutAsync(),
                    bindings.DisplayFor(KeybindingCommand.ResetWorkbenchLayout),
                    Binding: KeybindingCommand.ResetWorkbenchLayout),
                new("accessibility.focus.next", "Accessibility", "Focus next workbench region",
                    () => { host.FocusNextRegion(); return ValueTask.CompletedTask; },
                    bindings.DisplayFor(KeybindingCommand.FocusNextRegion),
                Binding: KeybindingCommand.FocusNextRegion),
            ]);
            commands.AddRange(WorkbenchPaletteCatalog.Build(
                host,
                bindings,
                needsTrust,
                needsGoal));

            PaletteCommand EditorCommand(
                string id,
                string title,
                KeybindingCommand command,
                string unavailable) => new(
                id, "Editor", title,
                async () => await host.InvokeActiveEditorCommandAsync(command),
                bindings.DisplayFor(command),
                host.CanInvokeActiveEditorCommand(command) ? null : unavailable,
                Binding: command);
        }

        PaletteCommandCatalog.RequireComplete(commands, includeWorkbenchCommands: workbench is not null);
        return commands;
    }

}
