using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Mcp;

namespace Harness.Presentation.Avalonia;

internal sealed partial class AvaloniaPresentationStore
{
    internal KeybindingValidationResult ValidateKeybindings(KeybindingUpdateRequest request) =>
        keybindingSettingsService?.Validate(request) ?? new(false,
            [new(KeybindingIssueKind.InvalidDocument, null,
                "Keybinding settings are unavailable.")], []);

    internal async ValueTask SaveKeybindingsAsync(
        KeybindingUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (keybindingSettingsService is null) return;
        await ApplyKeybindingChangeAsync(
            () => keybindingSettingsService.SaveAsync(request, cancellationToken),
            "Saving keybindings…", cancellationToken);
    }

    internal async ValueTask ResetKeybindingsAsync(CancellationToken cancellationToken)
    {
        if (keybindingSettingsService is null) return;
        await ApplyKeybindingChangeAsync(
            () => keybindingSettingsService.ResetAsync(cancellationToken),
            "Restoring default keybindings…", cancellationToken);
    }

    internal async ValueTask<string?> ExportKeybindingsAsync(CancellationToken cancellationToken)
    {
        if (keybindingSettingsService is null) return null;
        try
        {
            string exported = await keybindingSettingsService.ExportAsync(cancellationToken);
            Publish(Current with
            {
                Settings = Current.Settings with { Status = "Keybindings exported to the document below." },
            });
            return exported;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Publish(Current with
            {
                Settings = Current.Settings with { Status = $"Keybindings were not exported: {exception.Message}" },
            });
            return null;
        }
    }

    internal async ValueTask ImportKeybindingsAsync(
        string document,
        CancellationToken cancellationToken)
    {
        if (keybindingSettingsService is null) return;
        await ApplyKeybindingChangeAsync(
            () => keybindingSettingsService.ImportAsync(document, cancellationToken),
            "Validating and importing keybindings…", cancellationToken);
    }

    private async ValueTask ApplyKeybindingChangeAsync(
        Func<ValueTask<KeybindingSettingsSnapshot>> change,
        string busyStatus,
        CancellationToken cancellationToken)
    {
        Publish(Current with
        {
            Settings = Current.Settings with { IsBusy = true, Status = busyStatus },
        });
        try
        {
            KeybindingSettingsSnapshot saved = await change();
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    KeybindingSettings = saved,
                    IsBusy = false,
                    Status = saved.Status,
                },
            });
        }
        catch (OperationCanceledException)
        {
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    IsBusy = false,
                    Status = "Keybinding change cancelled.",
                },
            });
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    IsBusy = false,
                    Status = $"Keybindings were not changed: {exception.Message}",
                },
            });
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal async ValueTask SaveEditorIntelligenceSettingsAsync(
        EditorIntelligencePreferences preferences,
        CancellationToken cancellationToken)
    {
        if (editorIntelligenceSettingsService is null)
        {
            return;
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                IsBusy = true,
                Status = "Saving editor intelligence settings…",
            },
        });
        try
        {
            EditorIntelligenceSettingsSnapshot saved =
                await editorIntelligenceSettingsService.SaveAsync(preferences, cancellationToken);
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    EditorIntelligenceSettings = saved,
                    IsBusy = false,
                    Status = "Editor intelligence settings saved.",
                },
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    IsBusy = false,
                    Status = $"Editor settings were not saved: {exception.Message}",
                },
            });
        }
    }

    internal async ValueTask SaveAgentToolExposureAsync(
        IReadOnlyList<AgentToolModuleId> modules, CancellationToken cancellationToken)
    {
        if (agentToolExposureSettingsService is null) return;
        AgentToolExposureSettings saved = await agentToolExposureSettingsService.SaveAsync(
            new(modules), cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            { AgentToolExposure = saved, Status = "Agent tool exposure defaults saved." }
        });
    }

    internal async ValueTask SaveInboundMcpAsync(
        InboundControlSettings settings,
        CancellationToken cancellationToken)
    {
        if (inboundMcpSettingsService is null) return;
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = true, Status = "Applying inbound MCP settings…" }
        });
        try
        {
            InboundMcpSettingsView snapshot = await inboundMcpSettingsService.SaveAsync(
                settings, cancellationToken);
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    InboundMcpSettings = snapshot,
                    IsBusy = false,
                    Status = snapshot.Status.IsRunning ? "Inbound MCP server is active." :
                    snapshot.Status.Error ?? "Inbound MCP server is disabled."
                }
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Publish(Current with
            {
                Settings = Current.Settings with
                { IsBusy = false, Status = exception.Message }
            });
        }
    }

    internal async ValueTask DisconnectInboundMcpClientAsync(
        InboundControlClientId clientId,
        CancellationToken cancellationToken)
    {
        if (inboundMcpSettingsService is null) return;
        InboundMcpSettingsView snapshot = await inboundMcpSettingsService.DisconnectAsync(
            clientId, cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            { InboundMcpSettings = snapshot, Status = $"Disconnected {clientId.Value}." }
        });
    }

    internal async ValueTask ResetInboundMcpEvaluationAsync(CancellationToken cancellationToken)
    {
        if (inboundMcpSettingsService is null) return;
        try
        {
            InboundControlEvaluationReset reset = await inboundMcpSettingsService
                .ResetEvaluationAsync(cancellationToken);
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    InboundMcpSettings = reset.Settings,
                    Status = $"Evaluation fixture reset to {reset.Head[..Math.Min(12, reset.Head.Length)]}; {reset.ChangedFiles} changes remain."
                }
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Publish(Current with { Settings = Current.Settings with { Status = exception.Message } });
        }
    }

}
