using Harness.BusinessLogic.Debugging;
using Microsoft.Extensions.Logging;

namespace Harness.Presentation.Avalonia;

internal sealed partial class AvaloniaPresentationStore
{
    internal async ValueTask InstallDebuggerAsync(CancellationToken cancellationToken)
    {
        if (developerDebuggerSettingsService is null) return;
        await ChangeDebuggerAsync(
            developerDebuggerSettingsService.InstallAsync,
            "Downloading and verifying the pinned debugger…",
            "Debugger installation cancelled.",
            cancellationToken);
    }

    internal async ValueTask RemoveDebuggerAsync(CancellationToken cancellationToken)
    {
        if (developerDebuggerSettingsService is null) return;
        await ChangeDebuggerAsync(
            developerDebuggerSettingsService.RemoveAsync,
            "Removing the managed debugger…",
            "Debugger removal cancelled.",
            cancellationToken);
    }

    internal async ValueTask RefreshDebuggerAsync(CancellationToken cancellationToken)
    {
        if (developerDebuggerSettingsService is null) return;
        await ChangeDebuggerAsync(
            developerDebuggerSettingsService.GetAsync,
            "Verifying debugger integrity…",
            "Debugger verification cancelled.",
            cancellationToken);
    }

    private async ValueTask ChangeDebuggerAsync(
        Func<CancellationToken, ValueTask<DebugAdapterStatus>> change,
        string busyStatus,
        string cancelledStatus,
        CancellationToken cancellationToken)
    {
        Publish(Current with
        {
            Settings = Current.Settings with { IsBusy = true, Status = busyStatus },
        });
        try
        {
            DebugAdapterStatus status = await change(cancellationToken);
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    DebugAdapter = status,
                    IsBusy = false,
                    Status = status.Summary,
                },
            });
        }
        catch (OperationCanceledException)
        {
            Publish(Current with
            {
                Settings = Current.Settings with { IsBusy = false, Status = cancelledStatus },
            });
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Managed debugger change failed");
            Publish(Current with
            {
                Settings = Current.Settings with
                {
                    IsBusy = false,
                    Status = $"Debugger change failed safely: {exception.Message}",
                },
            });
        }
    }
}
