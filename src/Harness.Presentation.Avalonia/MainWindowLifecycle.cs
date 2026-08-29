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
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia;

internal sealed partial class MainWindow : Window
{
    private void ShowConversation()
    {
        if (workbench?.ShowConversation() is true)
        {
            Dispatcher.UIThread.Post(() => composer.Focus());
        }
    }

    private async Task ShowDialogAsync(Window dialog) => await dialog.ShowDialog(this);

    private async Task ShowSettingsAsync() =>
        await new SettingsWindow(store, cancellationToken).ShowDialog(this);

    private async Task ShowProjectUserSecretsAsync()
    {
        WorkspaceView? active = store.Current.Workspaces.Registered
            .FirstOrDefault(workspace => workspace.IsActive);
        if (active is null || !active.IsTrusted)
        {
            status.Severity = StatusSeverity.Warning;
            status.Message = active is null
                ? "Open a workspace before managing project User Secrets."
                : "Trust the workspace before managing project User Secrets.";
            return;
        }

        await new ProjectUserSecretsDialog(
            projectUserSecretsService,
            new WorkspaceId(active.Id),
            cancellationToken).ShowDialog(this);
    }

    internal async ValueTask<InboundUiActionResult> ActivateInboundUiAsync(InboundUiActionId action)
    {
        bool applied = action.Value switch
        {
            "chat.show" => ShowConversationForInbound(),
            "panel.files" => workbench?.ShowFiles() == true,
            "panel.git" => workbench?.ShowGit() == true,
            "panel.problems" => workbench?.ShowProblems() == true,
            "panel.output" => workbench?.ShowRunOutput() == true,
            "settings.open" => await OpenSettingsForInboundAsync(),
            _ => false,
        };
        return applied
            ? new(action, true, null, null)
            : new(action, false, "ui_action_unavailable", "The allowlisted Harness action is unavailable.");
    }

    internal ValueTask<InboundUiActionResult> OpenInboundDocumentAsync(
        InboundUiDocumentRequest request) => workbench is null
        ? ValueTask.FromResult(new InboundUiActionResult(new("document.open"), false,
            "workbench_unavailable", "The workbench is unavailable."))
        : workbench.OpenInboundDocumentAsync(request);

    internal IReadOnlyList<InboundOpenDocumentView> InboundOpenDocuments =>
        workbench?.InboundOpenDocuments ?? [];

    private bool ShowConversationForInbound()
    {
        ShowConversation();
        return true;
    }

    private async ValueTask<bool> OpenSettingsForInboundAsync()
    {
        await ShowSettingsAsync();
        return true;
    }

    private async Task ShowSemanticContextAsync()
    {
        if (store.Current.Goals.SelectedGoal is not { } goal)
        {
            return;
        }

        await store.RefreshSemanticStatusAsync(goal.Id, cancellationToken);
        await new SemanticContextDialog(store, goal, cancellationToken).ShowDialog(this);
    }

    private async void OnOpened(object? sender, EventArgs eventArgs)
    {
        if (loaded)
        {
            return;
        }

        loaded = true;
        themeController.Attach(this);
        await store.LoadAsync(cancellationToken);
        if (workbench is not null)
        {
            await workbench.RestoreLayoutAsync();
            await workbench.RefreshAsync();
        }
        if (store.Current.Workspaces.Registered.Any(item => item.IsActive))
        {
            composer.Focus();
        }
        else
        {
            openWorkspace.Focus();
        }
    }

    private async Task ShowWorkspaceDialogAsync(bool browseImmediately)
    {
        WorkspaceDialog dialog = new(
            store,
            cancellationToken,
            browseOnOpen: browseImmediately,
            prepareWorkspaceChange: PrepareWorkspaceChangeAsync);
        await dialog.ShowDialog(this);
    }

    private async Task ShowWorkspaceDialogAtAsync(string path)
    {
        store.SetRepositoryPath(path);
        await ShowWorkspaceDialogAsync(browseImmediately: false);
    }

    private async Task<bool> PrepareWorkspaceChangeAsync() =>
        workbench is null || await workbench.PrepareForWorkspaceChangeAsync();

    private async void OnClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (closingAfterLayoutSave || workbench is null)
        {
            return;
        }

        eventArgs.Cancel = true;
        if (!await workbench.PrepareForShutdownAsync())
        {
            return;
        }

        await workbench.SaveLayoutAsync(CancellationToken.None);
        closingAfterLayoutSave = true;
        Close();
    }

}
