using Avalonia.Automation;
using Avalonia.Controls;

namespace Harness.UI.Avalonia;

public sealed class AccessibleSplitter : GridSplitter
{
    public AccessibleSplitter()
    {
        Focusable = true;
        AutomationProperties.SetHelpText(
            this,
            "Use the arrow keys to resize the adjacent panels.");
    }
}
