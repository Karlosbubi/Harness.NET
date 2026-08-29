using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Harness.BusinessLogic.Debugging;

namespace Harness.Presentation.Avalonia;

internal sealed partial class SettingsWindow
{
    private Control DebuggerPage()
    {
        DebugAdapterStatus? status = settingsState.DebugAdapter;
        string state = status is null
            ? "Debugger management is unavailable in this host."
            : $"{status.Availability} · NetCoreDbg {status.Version.Value} · {status.Platform.Value}\n" +
              status.Summary;

        Button install = new()
        {
            Content = status?.Availability is DebugAdapterAvailability.Corrupt
                ? "Repair verified debugger"
                : "Install verified debugger",
            IsEnabled = status?.CanInstall == true && !settingsState.IsBusy,
        };
        install.Classes.Add("accent");
        AutomationProperties.SetName(install, "Install or repair verified .NET debugger");
        install.Click += async (_, _) => await store.InstallDebuggerAsync(cancellationToken);

        Button verify = new()
        {
            Content = "Verify integrity",
            IsEnabled = status is not null && !settingsState.IsBusy,
        };
        verify.Classes.Add("command");
        AutomationProperties.SetName(verify, "Verify .NET debugger integrity");
        verify.Click += async (_, _) => await store.RefreshDebuggerAsync(cancellationToken);

        Button remove = new()
        {
            Content = "Remove managed debugger",
            IsEnabled = status?.CanRemove == true && !settingsState.IsBusy,
        };
        remove.Classes.Add("danger");
        AutomationProperties.SetName(remove, "Remove managed .NET debugger");
        remove.Click += async (_, _) => await store.RemoveDebuggerAsync(cancellationToken);

        return Page(
            "Debugger",
            "Harness.NET uses one pinned, application-private .NET debug adapter. Installation is explicit; version, license, archive, and every installed payload are integrity checked.",
            new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new Border
                    {
                        Classes = { "card" },
                        Child = new TextBlock
                        {
                            Text = state,
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                    new TextBlock
                    {
                        Text = "The adapter is downloaded from Samsung's tagged GitHub release and its MIT license is retained beside the binaries. Harness never searches PATH, accepts a custom executable, or opens a debugger network listener.",
                        Classes = { "muted" },
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children = { install, verify, remove },
                    },
                },
            });
    }
}
