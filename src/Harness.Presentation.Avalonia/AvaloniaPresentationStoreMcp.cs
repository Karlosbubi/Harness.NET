using Harness.BusinessLogic.Mcp;
using Microsoft.Extensions.Logging;

namespace Harness.Presentation.Avalonia;

internal sealed partial class AvaloniaPresentationStore
{
    internal async ValueTask RefreshMcpAsync(CancellationToken cancellationToken)
    {
        if (mcpSettingsService is null)
        {
            Publish(Current with
            {
                Settings = Current.Settings with { Status = "MCP configuration is unavailable." },
            });
            return;
        }

        Publish(Current with
        {
            Settings = Current.Settings with { IsBusy = true, Status = "Discovering MCP tools…" },
        });
        try
        {
            McpSettingsSnapshot snapshot = await mcpSettingsService.RefreshAsync(cancellationToken);
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    McpSettings = snapshot,
                    IsBusy = false,
                    Status = "MCP discovery refreshed without inference.",
                },
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "MCP discovery failed");
            Publish(Current with
            {
                Settings = Current.Settings with { IsBusy = false, Status = exception.Message },
            });
        }
    }

    internal async ValueTask SaveMcpConnectionAsync(
        McpConnectionSettingsUpdate request,
        CancellationToken cancellationToken)
    {
        if (mcpSettingsService is null)
        {
            Publish(Current with
            {
                Settings = Current.Settings with { Status = "MCP configuration is unavailable." },
            });
            return;
        }

        Publish(Current with
        {
            Settings = Current.Settings with { IsBusy = true, Status = "Saving MCP connection…" },
        });
        try
        {
            McpSettingsResult result = await mcpSettingsService.SaveAsync(request, cancellationToken);
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    McpSettings = result.Snapshot ?? Current.Settings.McpSettings,
                    IsBusy = false,
                    Status = result.Error ?? "MCP connection saved. Restart Harness.NET to apply it.",
                },
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "MCP connection save failed");
            Publish(Current with
            {
                Settings = Current.Settings with { IsBusy = false, Status = exception.Message },
            });
        }
    }

    internal async ValueTask DeleteMcpConnectionAsync(
        McpConnectionName name,
        CancellationToken cancellationToken)
    {
        if (mcpSettingsService is null)
        {
            return;
        }

        Publish(Current with
        {
            Settings = Current.Settings with { IsBusy = true, Status = "Removing MCP connection…" },
        });
        try
        {
            McpSettingsResult result = await mcpSettingsService.DeleteAsync(name, cancellationToken);
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    McpSettings = result.Snapshot ?? Current.Settings.McpSettings,
                    IsBusy = false,
                    Status = result.Error ?? "MCP connection removed. Restart Harness.NET to apply it.",
                },
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "MCP connection removal failed");
            Publish(Current with
            {
                Settings = Current.Settings with { IsBusy = false, Status = exception.Message },
            });
        }
    }

}
