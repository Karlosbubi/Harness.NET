using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace Harness.UI.Avalonia;

public sealed class AccessibleIconButton : Button
{
    public static readonly StyledProperty<string?> AccessibleNameProperty =
        AvaloniaProperty.Register<AccessibleIconButton, string?>(nameof(AccessibleName));

    static AccessibleIconButton()
    {
        AccessibleNameProperty.Changed.AddClassHandler<AccessibleIconButton>((button, change) =>
            AutomationProperties.SetName(button, change.NewValue as string));
    }

    public AccessibleIconButton()
    {
        MinWidth = 36;
        MinHeight = 36;
        Focusable = true;
    }

    public string? AccessibleName
    {
        get => GetValue(AccessibleNameProperty);
        set => SetValue(AccessibleNameProperty, value);
    }
}
