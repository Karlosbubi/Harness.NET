using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace Harness.UI.Avalonia;

public sealed class StatusIndicator : TextBlock
{
    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<StatusIndicator, string>(nameof(Message), string.Empty);
    public static readonly StyledProperty<StatusSeverity> SeverityProperty =
        AvaloniaProperty.Register<StatusIndicator, StatusSeverity>(nameof(Severity));

    static StatusIndicator()
    {
        MessageProperty.Changed.AddClassHandler<StatusIndicator>((indicator, _) => indicator.Refresh());
        SeverityProperty.Changed.AddClassHandler<StatusIndicator>((indicator, _) => indicator.Refresh());
    }

    public StatusIndicator()
    {
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        Refresh();
    }

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public StatusSeverity Severity
    {
        get => GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    public void RefreshTheme() => Refresh();

    private void Refresh()
    {
        (string prefix, UiThemeColorToken token) = Severity switch
        {
            StatusSeverity.Information => ("ℹ ", UiThemeColorToken.Info),
            StatusSeverity.Success => ("✓ ", UiThemeColorToken.Success),
            StatusSeverity.Warning => ("⚠ ", UiThemeColorToken.Warning),
            StatusSeverity.Error => ("✕ ", UiThemeColorToken.Danger),
            _ => (string.Empty, UiThemeColorToken.TextMuted),
        };
        Text = prefix + Message;
        if (Application.Current?.TryFindResource(
                HarnessThemeResources.Key(token), out object? value) is true &&
            value is IBrush brush)
        {
            Foreground = brush;
        }
    }
}
