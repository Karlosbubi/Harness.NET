using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Appearance;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Workspaces;
using Microsoft.Extensions.Logging;

namespace Harness.Presentation.Avalonia;

internal sealed partial class AvaloniaPresentationStore
{
    internal void SetComposerText(string value) =>
        Publish(Current with { ComposerText = value });

    internal async ValueTask SubmitComposerAsync(CancellationToken cancellationToken)
    {
        if (Current.IsStreaming || string.IsNullOrWhiteSpace(Current.ComposerText))
        {
            return;
        }

        if (Current.Goals.SelectedGoal is not null)
        {
            await SubmitAsync(cancellationToken);
            return;
        }

        WorkspaceView? workspace = ActiveWorkspace(Current.Workspaces.Registered);
        if (workspace is null)
        {
            Publish(Current with { Error = "Open a workspace before creating a goal." });
            return;
        }

        if (!workspace.IsTrusted)
        {
            Publish(Current with { Error = "Trust the active workspace before creating a goal." });
            return;
        }

        string objective = Current.ComposerText.Trim();
        await CreateGoalAsync(
            new(
                workspace.Id,
                GoalTitle(objective),
                objective,
                new ReviewCycleLimit(3),
                Current.Settings.RemoteSpendPreference.ToGoalBudget()),
            cancellationToken);
        if (Current.Goals.SelectedGoal is not null)
        {
            Publish(Current with { ComposerText = string.Empty, Error = null });
        }
    }

    internal async ValueTask SubmitAsync(CancellationToken cancellationToken)
    {
        if (Current.IsStreaming || string.IsNullOrWhiteSpace(Current.ComposerText))
        {
            return;
        }

        await commandGate.WaitAsync(cancellationToken);
        try
        {
            if (Current.IsStreaming)
            {
                return;
            }

            string instruction = Current.ComposerText.Trim();
            submission = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Publish(Current with
            {
                ComposerText = string.Empty,
                IsStreaming = true,
                Error = null,
            });
            logger.LogInformation("Avalonia conversation submission started");
            await foreach (DashboardSnapshot dashboard in dashboardService
                               .SubmitAsync(instruction, submission.Token)
                               .WithCancellation(submission.Token))
            {
                Publish(Current with { Dashboard = dashboard });
            }
        }
        catch (OperationCanceledException) when (submission?.IsCancellationRequested is true)
        {
            logger.LogInformation("Avalonia conversation submission cancelled");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Avalonia conversation submission failed");
            Publish(Current with { Error = exception.Message });
        }
        finally
        {
            submission?.Dispose();
            submission = null;
            Publish(Current with { IsStreaming = false });
            commandGate.Release();
        }
    }

    internal void CancelSubmission() => submission?.Cancel();

    internal async ValueTask RefreshProviderAsync(CancellationToken cancellationToken) =>
        await UpdateDashboardAsync(
            () => dashboardService.RefreshProviderAsync(cancellationToken),
            "Provider refresh");

    internal async ValueTask SelectModelAsync(string model, CancellationToken cancellationToken) =>
        await UpdateDashboardAsync(
            () => dashboardService.SelectModelAsync(model, cancellationToken),
            "Model selection");

    internal async ValueTask RefreshThemesAsync(CancellationToken cancellationToken)
    {
        AppearanceSnapshot appearance = await appearanceService.GetAsync(cancellationToken);
        Publish(Current with { Appearance = appearance, Error = null });
    }

    internal async ValueTask SelectThemeAsync(string themeId, CancellationToken cancellationToken)
    {
        AppearanceSelectionResult result = await appearanceService.SelectAsync(
            new(themeId), cancellationToken);
        Publish(Current with
        {
            Appearance = result.Snapshot,
            Error = result.Error,
        });
    }

    internal async ValueTask DiscoverAgentDefaultsAsync(CancellationToken cancellationToken)
    {
        Publish(Current with
        {
            Settings = Current.Settings with { IsBusy = true, Status = "Discovering chat models…" },
            Error = null,
        });
        try
        {
            AgentDefaultsSnapshot snapshot = await agentDefaultsService
                .DiscoverAvailableAsync(cancellationToken);
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    AgentDefaults = snapshot,
                    IsBusy = false,
                    Status = snapshot.Issues.Count == 0
                        ? $"Discovered {snapshot.Models.Count} chat model(s)."
                        : $"Discovered {snapshot.Models.Count} chat model(s) with " +
                          $"{snapshot.Issues.Count} provider issue(s).",
                },
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Agent default model discovery failed");
            Publish(Current with
            {
                Settings = Current.Settings with { IsBusy = false, Status = exception.Message },
                Error = exception.Message,
            });
        }
    }

    internal async ValueTask UpdateModelProviderAsync(
        ModelProviderSettingsUpdate request,
        CancellationToken cancellationToken)
    {
        if (modelProviderSettingsService is null)
        {
            Publish(Current with
            {
                Settings = Current.Settings with { Status = "Provider configuration is unavailable." },
            });
            return;
        }

        Publish(Current with
        {
            Settings = Current.Settings with { IsBusy = true, Status = "Saving provider configuration…" },
        });
        try
        {
            ModelProviderSettingsResult result = await modelProviderSettingsService.UpdateAsync(
                request,
                cancellationToken);
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    ProviderSettings = result.Snapshot ?? Current.Settings.ProviderSettings,
                    IsBusy = false,
                    Status = result.Error ?? "Provider configuration saved. Restart Harness.NET to apply it.",
                },
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Provider configuration update failed");
            Publish(Current with
            {
                Settings = Current.Settings with { IsBusy = false, Status = exception.Message },
            });
        }
    }

    internal async ValueTask SetModelProviderCredentialAsync(
        ModelProviderCredentialUpdate request,
        CancellationToken cancellationToken)
    {
        if (modelProviderSettingsService is null)
        {
            Publish(Current with
            {
                Settings = Current.Settings with { Status = "Provider credentials are unavailable." },
            });
            return;
        }

        Publish(Current with
        {
            Settings = Current.Settings with { IsBusy = true, Status = "Saving provider credential…" },
        });
        try
        {
            ModelProviderSettingsResult result = await modelProviderSettingsService.SetCredentialAsync(
                request,
                cancellationToken);
            ModelProviderSettingsView? provider = result.Snapshot?.Providers.FirstOrDefault(item =>
                item.Provider == request.Provider);
            bool canRefreshActiveProvider = provider is { RequiresRestart: false };
            AgentDefaultsSnapshot? defaults = canRefreshActiveProvider
                ? await agentDefaultsService.DiscoverAvailableAsync(cancellationToken)
                : Current.Settings.AgentDefaults;
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    ProviderSettings = result.Snapshot ?? Current.Settings.ProviderSettings,
                    AgentDefaults = defaults,
                    IsBusy = false,
                    Status = result.Error ?? (canRefreshActiveProvider
                        ? "Provider credential saved to Secret Service; catalog refreshed."
                        : "Provider credential saved to Secret Service. Restart Harness.NET to activate the pending provider configuration."),
                },
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Provider credential update failed");
            Publish(Current with
            {
                Settings = Current.Settings with { IsBusy = false, Status = exception.Message },
            });
        }
    }

    internal async ValueTask UpdateAgentDefaultAsync(AgentRole role, GoalModelCandidate candidate,
        AgentReasoningPolicy reasoningPolicy,
        CancellationToken cancellationToken)
    {
        Publish(Current with
        {
            Settings = Current.Settings with { IsBusy = true, Status = $"Saving {role} defaults…" },
            Error = null,
        });
        try
        {
            AgentRoleDefaultUpdateResult result = await agentDefaultsService.UpdateAsync(new(
                role,
                candidate.Provider,
                candidate.Model,
                reasoningPolicy), cancellationToken);
            if (result.Value is null)
            {
                Publish(Current with
                {
                    Settings = Current.Settings with { IsBusy = false, Status = result.Error },
                    Error = result.Error,
                });
                return;
            }

            AgentDefaultsSnapshot current = Current.Settings.AgentDefaults ??
                new([], [], [], [], []);
            AgentDefaultsSnapshot updated = current with
            {
                Roles = current.Roles
                    .Where(item => item.Role != role)
                    .Append(result.Value)
                    .OrderBy(item => item.Role)
                    .ToArray(),
                DefaultIssues = current.DefaultIssues
                    .Where(issue => issue.Role != role)
                    .ToArray(),
            };
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    AgentDefaults = updated,
                    IsBusy = false,
                    Status = $"Saved {role} defaults.",
                },
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Agent default update failed for {Role}", role);
            Publish(Current with
            {
                Settings = Current.Settings with { IsBusy = false, Status = exception.Message },
                Error = exception.Message,
            });
        }
    }

    internal async ValueTask UpdateRemoteSpendPreferenceAsync(
        RemoteSpendPreference preference,
        CancellationToken cancellationToken)
    {
        if (remoteSpendPreferenceService is null)
        {
            Publish(Current with
            {
                Settings = Current.Settings with { Status = "Remote-spend preferences are unavailable." },
            });
            return;
        }

        Publish(Current with
        {
            Settings = Current.Settings with
            {
                IsBusy = true,
                Status = "Saving default remote-spend policy…",
            },
        });
        RemoteSpendPreferenceResult result = await remoteSpendPreferenceService.UpdateAsync(
            preference,
            cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                RemoteSpendPreference = result.Preference,
                IsBusy = false,
                Status = result.Error ?? "Default remote-spend policy saved.",
            },
            Error = result.Error,
        });
    }

}
