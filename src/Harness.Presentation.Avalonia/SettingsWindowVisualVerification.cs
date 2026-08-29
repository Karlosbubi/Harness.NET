using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Appearance;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Editor;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Mcp;
using Harness.BusinessLogic.Research;
using Harness.BusinessLogic.VisualCapture;

namespace Harness.Presentation.Avalonia;

internal sealed partial class SettingsWindow
{
    private Control VisualVerificationPage()
    {
        VisualCaptureSettingsSnapshot? snapshot = settingsState.VisualCaptureSettings;
        VisualCapturePreferences preferences = snapshot?.Preferences ?? VisualCapturePreferences.Default;
        CheckBox enabled = new()
        {
            Content = "Allow consented single-frame capture",
            IsChecked = preferences.IsEnabled,
            IsEnabled = snapshot is not null && !settingsState.IsBusy,
        };
        AutomationProperties.SetName(enabled, "Enable visual verification capture");
        NumericUpDown maximumMiB = ProviderNumber(
            (int)(preferences.MaximumBytes.Value / (1024 * 1024)), 1, 16,
            "Maximum visual capture size in MiB");
        NumericUpDown retentionDays = ProviderNumber(
            preferences.RetentionDays.Value, 1, 90, "Visual capture retention days");
        NumericUpDown maximumPerGoal = ProviderNumber(
            preferences.MaximumPerGoal.Value, 1, 100, "Maximum visual captures per goal");
        CheckBox remote = new()
        {
            Content = "Allow remote models to receive captured images",
            IsChecked = preferences.AllowRemoteModelAccess,
            IsEnabled = snapshot is not null && !settingsState.IsBusy,
        };
        AutomationProperties.SetName(remote, "Allow remote model access to visual captures");
        Button save = new() { Content = "Save visual verification settings", IsEnabled = snapshot is not null && !settingsState.IsBusy };
        save.Classes.Add("accent");
        AutomationProperties.SetName(save, "Save visual verification settings");
        save.Click += async (_, _) => await store.SaveVisualCaptureSettingsAsync(new(
            enabled.IsChecked == true,
            new(decimal.ToInt64(maximumMiB.Value ?? 5) * 1024 * 1024),
            new(decimal.ToInt32(retentionDays.Value ?? 7)),
            new(decimal.ToInt32(maximumPerGoal.Value ?? 20)),
            remote.IsChecked == true), cancellationToken);

        ComboBox target = new()
        {
            ItemsSource = Enum.GetValues<VisualCaptureTarget>(),
            SelectedItem = VisualCaptureTarget.UserSelection,
            MinWidth = 220,
        };
        AutomationProperties.SetName(target, "Visual capture target");
        Button capture = new()
        {
            Content = "Capture one frame…",
            IsEnabled = snapshot is not null && !settingsState.IsBusy,
        };
        capture.Classes.Add("accent");
        AutomationProperties.SetName(capture, "Capture one visual verification frame");
        capture.Click += async (_, _) => await store.CaptureVisualAsync(
            target.SelectedItem is VisualCaptureTarget selected
                ? selected
                : VisualCaptureTarget.UserSelection,
            new VisualCaptureUiScale(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1),
            parentWindow: null,
            cancellationToken);
        Button refresh = new() { Content = "Refresh goal evidence" };
        refresh.Classes.Add("command");
        refresh.Click += async (_, _) => await store.RefreshVisualCapturesAsync(cancellationToken);

        ComboBox captures = new()
        {
            ItemsSource = settingsState.VisualCaptures.Select(item => new VisualCaptureChoice(item)).ToArray(),
            MinWidth = 360,
            PlaceholderText = "Select a stored frame",
        };
        AutomationProperties.SetName(captures, "Stored visual captures for selected goal");
        captures.SelectionChanged += async (_, _) =>
        {
            if (captures.SelectedItem is VisualCaptureChoice choice)
            {
                await store.InspectVisualCaptureAsync(choice.Capture.Id, cancellationToken);
            }
        };
        Button delete = new() { Content = "Delete selected frame", IsEnabled = settingsState.SelectedVisualCapture is not null };
        delete.Classes.Add("danger");
        AutomationProperties.SetName(delete, "Delete selected visual capture");
        delete.Click += async (_, _) =>
        {
            if (settingsState.SelectedVisualCapture is { } content)
            {
                await store.DeleteVisualCaptureAsync(content.Capture.Id, cancellationToken);
            }
        };

        StackPanel preview = new() { Spacing = 8 };
        if (settingsState.SelectedVisualCapture is { } selectedCapture)
        {
            byte[] bytes = Convert.FromBase64String(selectedCapture.Content.Base64);
            Image exactFrame = new()
            {
                Source = new Bitmap(new MemoryStream(bytes, writable: false)),
                MaxHeight = 360,
                Stretch = Stretch.Uniform,
            };
            AutomationProperties.SetName(exactFrame, "Exact stored visual capture frame");
            preview.Children.Add(exactFrame);
            VisualCaptureView item = selectedCapture.Capture;
            preview.Children.Add(new TextBlock
            {
                Text = $"Goal {item.GoalId.Value} · {item.CreatedAt.LocalDateTime:G}\n" +
                       $"Initiator: {item.Initiator} · Action: {item.RelatedAction.Value}\n" +
                       $"Application: {item.ApplicationIdentity.Value} · Target: {item.Target}\n" +
                       $"{item.PixelSize.Width}×{item.PixelSize.Height} · {item.Bytes.Value:N0} bytes · {item.MediaType.Value}\n" +
                       $"SHA-256 {item.Sha256.Value}\n" +
                       $"Window/display identity: {item.IdentityState}; scale: {item.ScaleState}" +
                       (item.UiScale is null ? string.Empty : $" ({item.UiScale.Value:0.##}×)") +
                       $"\nModel access: local; remote {(settingsState.VisualCaptureSettings?.Preferences.AllowRemoteModelAccess == true ? "enabled" : "disabled")}",
                Classes = { "muted" },
                TextWrapping = TextWrapping.Wrap,
            });
        }
        else
        {
            preview.Children.Add(new TextBlock
            {
                Text = "No stored frame selected. The preview uses the exact bytes agents inspect.",
                Classes = { "muted" },
                TextWrapping = TextWrapping.Wrap,
            });
        }

        string availability = snapshot is null
            ? "Visual capture service unavailable."
            : snapshot.Availability.IsAvailable
                ? $"XDG portal v{snapshot.Availability.PortalVersion} available · targets: {string.Join(", ", snapshot.Availability.AvailableTargets)}"
                : $"Portal unavailable · {snapshot.Availability.Error}";
        return Page("Visual verification",
            "Capture one frame through the XDG Desktop Portal. Every request shows portal consent; Harness.NET cannot capture in the background or control input.",
            new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new Border { Classes = { "card" }, Child = new TextBlock { Text = availability + "\n" + (snapshot?.PrivateStorageDescription ?? string.Empty), TextWrapping = TextWrapping.Wrap } },
                    enabled,
                    new TextBlock { Text = "Maximum frame size (MiB)", FontWeight = FontWeight.SemiBold }, maximumMiB,
                    new TextBlock { Text = "Retention (days)", FontWeight = FontWeight.SemiBold }, retentionDays,
                    new TextBlock { Text = "Maximum frames per goal", FontWeight = FontWeight.SemiBold }, maximumPerGoal,
                    new Border { Classes = { "card", "attention" }, Child = new StackPanel { Spacing = 6, Children = { remote, new TextBlock { Text = "Off by default. Enabling this permits selected remote model providers to receive exact screenshot bytes.", TextWrapping = TextWrapping.Wrap } } } },
                    save,
                    new Separator(),
                    new TextBlock { Text = "Selected goal evidence", FontSize = 18, FontWeight = FontWeight.SemiBold },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { target, capture, refresh } },
                    captures,
                    delete,
                    new Border { Classes = { "card" }, Child = preview },
                    new TextBlock { Text = settingsState.Status ?? string.Empty, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap },
                },
            });
    }

}
