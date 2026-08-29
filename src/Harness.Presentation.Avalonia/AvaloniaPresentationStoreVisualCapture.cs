using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.VisualCapture;

namespace Harness.Presentation.Avalonia;

internal sealed partial class AvaloniaPresentationStore
{
    internal async ValueTask SaveVisualCaptureSettingsAsync(
        VisualCapturePreferences preferences,
        CancellationToken cancellationToken)
    {
        if (visualCaptureService is null)
        {
            Publish(Current with
            {
                Settings = Current.Settings with
                { Status = "Visual capture is unavailable." }
            });
            return;
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = true, Status = "Saving visual verification settings…" }
        });
        VisualCaptureSettingsResult result = await visualCaptureService.SaveSettingsAsync(
            preferences, cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                VisualCaptureSettings = result.Snapshot ?? Current.Settings.VisualCaptureSettings,
                IsBusy = false,
                Status = result.Error ?? "Visual verification settings saved.",
            }
        });
    }

    internal async ValueTask CaptureVisualAsync(
        VisualCaptureTarget target,
        VisualCaptureUiScale? uiScale,
        VisualCaptureParentWindow? parentWindow,
        CancellationToken cancellationToken)
    {
        GoalId? goalId = Current.Goals.SelectedGoalId;
        if (visualCaptureService is null || goalId is null)
        {
            Publish(Current with
            {
                Settings = Current.Settings with
                { Status = "Select a goal before capturing visual evidence." }
            });
            return;
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = true, Status = "Waiting for portal consent…" }
        });
        VisualCaptureResult result = await visualCaptureService.CaptureAsync(new(
            goalId,
            new ToolCorrelationId($"developer-capture-{Guid.NewGuid():N}"),
            VisualCaptureInitiator.Developer,
            new("Manual visual verification"),
            new("Harness.NET"),
            target,
            TimeProvider.System.GetUtcNow(),
            parentWindow,
            uiScale), cancellationToken);
        await RefreshVisualCapturesAsync(cancellationToken);
        if (result.Capture is not null)
        {
            await InspectVisualCaptureAsync(result.Capture.Id, cancellationToken);
        }
        Publish(Current with
        {
            Settings = Current.Settings with
            { IsBusy = false, Status = result.Error ?? "Visual evidence captured." }
        });
    }

    internal async ValueTask RefreshVisualCapturesAsync(CancellationToken cancellationToken)
    {
        GoalId? goalId = Current.Goals.SelectedGoalId;
        if (visualCaptureService is null || goalId is null)
        {
            return;
        }
        VisualCaptureListResult result = await visualCaptureService.ListAsync(goalId, cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            { VisualCaptures = result.Captures, Status = result.Error ?? Current.Settings.Status }
        });
    }

    internal async ValueTask InspectVisualCaptureAsync(
        VisualCaptureId captureId,
        CancellationToken cancellationToken)
    {
        GoalId? goalId = Current.Goals.SelectedGoalId;
        if (visualCaptureService is null || goalId is null)
        {
            return;
        }
        VisualCaptureInspectionResult result = await visualCaptureService.InspectAsync(
            goalId, captureId, VisualCaptureModelAccess.Local, cancellationToken);
        Publish(Current with
        {
            Settings = Current.Settings with
            {
                SelectedVisualCapture = result.Content,
                Status = result.Error ?? "Showing the exact stored frame available to agents.",
            }
        });
    }

    internal async ValueTask DeleteVisualCaptureAsync(
        VisualCaptureId captureId,
        CancellationToken cancellationToken)
    {
        GoalId? goalId = Current.Goals.SelectedGoalId;
        if (visualCaptureService is null || goalId is null)
        {
            return;
        }
        await visualCaptureService.DeleteAsync(goalId, captureId, cancellationToken);
        Publish(Current with { Settings = Current.Settings with { SelectedVisualCapture = null } });
        await RefreshVisualCapturesAsync(cancellationToken);
    }

}
