using System.Runtime.CompilerServices;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Harness.Presentation.Avalonia;

internal static class AccessibilityTreeSemantics
{
    private static readonly ConditionalWeakTable<Window, WindowState> WindowStates = new();
    private static bool registered;

    internal static void Register()
    {
        if (registered)
        {
            return;
        }

        registered = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>(OnWindowOpened);
    }

    internal static void Apply(Control root)
    {
        ApplyControl(root);
        foreach (Control control in root.GetVisualDescendants().OfType<Control>())
        {
            ApplyControl(control);
        }
    }

    private static void OnWindowOpened(Window window, RoutedEventArgs _)
    {
        WindowState state = WindowStates.GetValue(window, static item => new(item));
        state.ApplyNow();
    }

    private static void ApplyControl(Control control)
    {
        if (!string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)))
        {
            return;
        }

        AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(control);
        if (peer.IsControlElement() || peer.IsContentElement())
        {
            return;
        }

        // Avalonia 12.1's Linux AT-SPI bridge currently exports raw peers without
        // applying IsControlElement/IsContentElement. It then falls back to the CLR
        // class name, causing visual-only Grid, Panel, presenter, and Dock wrappers
        // to be spoken. Keep their descendants in the tree while making the wrapper
        // itself anonymous and role-neutral.
        if (AutomationProperties.GetClassNameOverride(control) is null)
        {
            AutomationProperties.SetClassNameOverride(control, string.Empty);
        }

        if (AutomationProperties.GetControlTypeOverride(control) is null)
        {
            AutomationProperties.SetControlTypeOverride(control, AutomationControlType.Custom);
        }
    }

    private sealed class WindowState
    {
        private readonly Window window;
        private bool pending;

        internal WindowState(Window window)
        {
            this.window = window;
            window.LayoutUpdated += OnLayoutUpdated;
            window.Closed += OnClosed;
        }

        internal void ApplyNow()
        {
            pending = false;
            Apply(window);
        }

        private void OnLayoutUpdated(object? sender, EventArgs e)
        {
            if (pending)
            {
                return;
            }

            pending = true;
            Dispatcher.UIThread.Post(ApplyIfOpen, DispatcherPriority.Background);
        }

        private void ApplyIfOpen()
        {
            pending = false;
            if (window.IsVisible)
            {
                Apply(window);
            }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            window.LayoutUpdated -= OnLayoutUpdated;
            window.Closed -= OnClosed;
        }
    }
}
