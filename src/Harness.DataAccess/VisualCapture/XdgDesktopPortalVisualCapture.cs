using Harness.DataAccess.VisualCapture.DBus;
using Tmds.DBus.Protocol;
using PortalRequest = Harness.DataAccess.VisualCapture.DBus.Request;

namespace Harness.DataAccess.VisualCapture;

internal sealed class XdgDesktopPortalVisualCapture : IVisualCapturePortal
{
    private const string Destination = "org.freedesktop.portal.Desktop";
    private const string DesktopPath = "/org/freedesktop/portal/desktop";

    public async ValueTask<PortalCaptureAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux() || DBusAddress.Session is null)
        {
            return new(false, 0, 0, "portal_unavailable",
                "An XDG desktop session bus is not available.");
        }

        try
        {
            using DBusConnection connection = new(DBusAddress.Session);
            await connection.ConnectAsync().AsTask().WaitAsync(cancellationToken);
            Screenshot screenshot = new(connection, Destination, DesktopPath);
            uint version = await screenshot.GetVersionAsync().WaitAsync(cancellationToken);
            uint targets = version >= 3
                ? await screenshot.GetAvailableTargetsAsync().WaitAsync(cancellationToken)
                : 0;
            return new(true, version, targets, null, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsPortalException(exception))
        {
            return AvailabilityFailure(exception);
        }
    }

    public async ValueTask<PortalCaptureResult> CaptureAsync(
        PortalCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        PortalCaptureAvailability availability = await GetAvailabilityAsync(cancellationToken);
        if (!availability.IsAvailable || DBusAddress.Session is null)
        {
            return new(PortalCaptureState.Unavailable, null, availability.InterfaceVersion,
                availability.AvailableTargets, availability.ErrorCode, availability.Error);
        }

        try
        {
            using DBusConnection connection = new(DBusAddress.Session);
            await connection.ConnectAsync().AsTask().WaitAsync(cancellationToken);
            Screenshot screenshot = new(connection, Destination, DesktopPath);
            string token = "harness_" + Guid.NewGuid().ToString("N");
            string sender = connection.UniqueName![1..].Replace('.', '_');
            string expectedPath = $"/org/freedesktop/portal/desktop/request/{sender}/{token}";
            TaskCompletionSource<(uint Response, Dictionary<string, VariantValue> Results)> response =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            PortalRequest portalRequest = new(connection, Destination, expectedPath);
            IDisposable observer = await portalRequest.WatchResponseAsync(
                (exception, value) =>
                {
                    if (exception is not null)
                    {
                        response.TrySetException(exception);
                    }
                    else
                    {
                        response.TrySetResult(value);
                    }
                },
                emitOnCapturedContext: false,
                ObserverFlags.None);
            try
            {
                using CancellationTokenRegistration registration = cancellationToken.Register(() =>
                {
                    response.TrySetCanceled(cancellationToken);
                    _ = portalRequest.CloseAsync();
                });
                Dictionary<string, VariantValue> options = new()
                {
                    ["handle_token"] = token,
                    ["modal"] = true,
                    ["interactive"] = true,
                };
                uint target = TargetValue(request.Target);
                if (availability.InterfaceVersion >= 3 && target != 0 &&
                    (availability.AvailableTargets & target) != 0)
                {
                    options["target"] = target;
                }

                ObjectPath returnedPath = await screenshot.ScreenshotAsync(
                    request.ParentWindow?.Value ?? string.Empty,
                    options).WaitAsync(cancellationToken);
                if (!returnedPath.ToString().Equals(expectedPath, StringComparison.Ordinal))
                {
                    observer.Dispose();
                    portalRequest = new(connection, Destination, returnedPath);
                    observer = await portalRequest.WatchResponseAsync(
                        (exception, value) =>
                        {
                            if (exception is not null)
                            {
                                response.TrySetException(exception);
                            }
                            else
                            {
                                response.TrySetResult(value);
                            }
                        },
                        emitOnCapturedContext: false,
                        ObserverFlags.None);
                }

                (uint code, Dictionary<string, VariantValue> results) =
                    await response.Task.WaitAsync(cancellationToken);
                if (code == 1)
                {
                    return new(PortalCaptureState.Cancelled, null,
                        availability.InterfaceVersion, availability.AvailableTargets,
                        "capture_cancelled", "The user cancelled the portal request.");
                }
                if (code != 0)
                {
                    return new(PortalCaptureState.Denied, null,
                        availability.InterfaceVersion, availability.AvailableTargets,
                        "capture_denied", "The desktop portal denied or ended the request.");
                }
                if (!results.TryGetValue("uri", out VariantValue value) ||
                    value.Type != VariantValueType.String ||
                    !Uri.TryCreate(value.GetString(), UriKind.Absolute, out Uri? uri))
                {
                    return new(PortalCaptureState.Failed, null,
                        availability.InterfaceVersion, availability.AvailableTargets,
                        "portal_result_invalid", "The portal returned no valid screenshot URI.");
                }
                return new(PortalCaptureState.Succeeded, uri,
                    availability.InterfaceVersion, availability.AvailableTargets, null, null);
            }
            finally
            {
                observer.Dispose();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(PortalCaptureState.Cancelled, null, availability.InterfaceVersion,
                availability.AvailableTargets, "capture_cancelled", "The capture was cancelled.");
        }
        catch (Exception exception) when (IsPortalException(exception))
        {
            PortalCaptureState state = IsDenied(exception)
                ? PortalCaptureState.Denied
                : PortalCaptureState.Unavailable;
            return new(state, null, availability.InterfaceVersion,
                availability.AvailableTargets,
                state is PortalCaptureState.Denied ? "capture_denied" : "portal_unavailable",
                exception.Message);
        }
    }

    private static uint TargetValue(PortalCaptureTarget target) => target switch
    {
        PortalCaptureTarget.UserSelection => 0,
        PortalCaptureTarget.Screen => 1,
        PortalCaptureTarget.Window => 2,
        PortalCaptureTarget.Area => 4,
        PortalCaptureTarget.ActiveWindow => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    private static PortalCaptureAvailability AvailabilityFailure(Exception exception) => new(
        false,
        0,
        0,
        "portal_unavailable",
        exception.Message);

    private static bool IsDenied(Exception exception) => exception is DBusErrorReplyException error &&
        (error.ErrorName.Contains("NotAllowed", StringComparison.OrdinalIgnoreCase) ||
         error.ErrorName.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase));

    private static bool IsPortalException(Exception exception) => exception is
        DBusExceptionBase or IOException or InvalidOperationException;
}
