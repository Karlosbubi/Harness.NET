using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Avalonia;
using Dock.Model.Controls;
using Dock.Model.Core;
using Harness.BusinessLogic.Editor;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed class WorkbenchNavigator
{
    private static readonly KeybindingCommand[] Commands =
    [
        KeybindingCommand.ShowFiles,
        KeybindingCommand.ShowGit,
        KeybindingCommand.ShowRunOutput,
        KeybindingCommand.ShowProblems,
        KeybindingCommand.FocusNextRegion,
    ];

    private readonly Factory factory;
    private readonly WorkbenchLayoutHost layout;
    private readonly DockControl control;
    private KeybindingSettingsSnapshot keybindings = KeybindingSettingsSnapshot.Default;
    private int focusRegionIndex = -1;

    internal WorkbenchNavigator(Factory factory, WorkbenchLayoutHost layout, DockControl control)
    {
        this.factory = factory;
        this.layout = layout;
        this.control = control;
        control.KeyDown += OnKeyDown;
        control.LayoutUpdated += (_, _) => ApplyAutomationNames();
    }

    internal Control? LastRequestedFocusTarget { get; private set; }

    internal void Update(KeybindingSettingsSnapshot next) => keybindings = next;

    internal bool ShowFiles() => ActivateTool(WorkbenchDockIds.FilesTool);
    internal bool ShowWorkspace() => ActivateTool(WorkbenchDockIds.NavigationTool);
    internal bool ShowConversation() => ActivateTool(WorkbenchDockIds.ConversationTool);
    internal bool ShowGit() => ActivateTool(WorkbenchDockIds.GitTool);
    internal bool ShowRunOutput() => ActivateTool(WorkbenchDockIds.RunOutputTool);
    internal bool ShowTerminal() => ActivateTool(WorkbenchDockIds.TerminalTool);
    internal bool ShowProblems() => ActivateTool(WorkbenchDockIds.ProblemsTool);

    internal bool FocusNextRegion()
    {
        string[] regions =
        [
            WorkbenchDockIds.FilesTool,
            WorkbenchDockIds.OverviewDocument,
            WorkbenchDockIds.GitTool,
            WorkbenchDockIds.ConversationTool,
            WorkbenchDockIds.RunOutputTool,
            WorkbenchDockIds.TerminalTool,
        ];
        focusRegionIndex = (focusRegionIndex + 1) % regions.Length;
        if (regions[focusRegionIndex] == WorkbenchDockIds.OverviewDocument)
        {
            IDockable target = layout.Documents.ActiveDockable ?? layout.Overview;
            factory.SetActiveDockable(target);
            FocusContext(target);
            return true;
        }
        return ActivateTool(regions[focusRegionIndex]);
    }

    internal void FocusContext(IDockable dockable)
    {
        if (dockable.Context is not Control context) return;
        Control? target = context.Focusable
            ? context
            : context.GetVisualDescendants().OfType<Control>()
                .FirstOrDefault(item => item.Focusable && item.IsEffectivelyVisible);
        LastRequestedFocusTarget = target;
        if (target is not null && !target.Focus())
            Dispatcher.UIThread.Post(() => target.Focus());
    }

    internal void RecordFocusRequest(Control control) => LastRequestedFocusTarget = control;

    private bool ActivateTool(string id)
    {
        IDockable? tool = layout.Find(id);
        bool visibleInOwner = tool?.Owner is IDock visibleOwner &&
                              visibleOwner.VisibleDockables?.Contains(tool) is true;
        if (!visibleInOwner && factory.RestoreDockable(id) is { } restored) tool = restored;
        if (tool is null) return false;
        visibleInOwner = tool.Owner is IDock restoredOwner &&
                         restoredOwner.VisibleDockables?.Contains(tool) is true;
        if (!visibleInOwner && layout.DefaultToolDock(id) is { } defaultOwner)
        {
            layout.RemoveFromSpecialCollections(tool);
            factory.AddDockable(defaultOwner, tool);
        }
        if (tool.Owner is IToolDock owner)
        {
            layout.RestoreAdaptiveProportion(owner);
            owner.IsExpanded = true;
        }
        factory.SetActiveDockable(tool);
        FocusContext(tool);
        return true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        KeybindingCommand? command = KeybindingInput.Match(args, keybindings, Commands);
        if (command is null) return;
        args.Handled = command switch
        {
            KeybindingCommand.ShowFiles => ShowFiles(),
            KeybindingCommand.ShowGit => ShowGit(),
            KeybindingCommand.ShowRunOutput => ShowRunOutput(),
            KeybindingCommand.ShowProblems => ShowProblems(),
            KeybindingCommand.FocusNextRegion => FocusNextRegion(),
            _ => false,
        };
    }

    private void ApplyAutomationNames()
    {
        foreach (DocumentTabStripItem tab in control.GetVisualDescendants()
                     .OfType<DocumentTabStripItem>())
        {
            if (tab.DataContext is IDockable { Title: { Length: > 0 } title })
            {
                AutomationProperties.SetAccessibilityView(tab, AccessibilityView.Content);
                SetAutomationName(tab, title);
            }
        }
        foreach (ToolChromeControl chrome in control.GetVisualDescendants().OfType<ToolChromeControl>())
            if (chrome.DataContext is IToolDock dock)
                SetAutomationName(chrome, $"{DockTitle(dock)} panel controls");
        foreach (ToolControl toolControl in control.GetVisualDescendants().OfType<ToolControl>())
            if (toolControl.DataContext is IToolDock dock)
                SetAutomationName(toolControl, $"{DockTitle(dock)} panel");
        foreach (ItemsControl itemsControl in control.GetVisualDescendants().OfType<ItemsControl>()
                     .Where(item => item.DataContext is IProportionalDock))
            SetAutomationName(itemsControl, "Workbench panel layout");
        foreach (Button button in control.GetVisualDescendants().OfType<Button>())
        {
            string? name = button.Name switch
            {
                "PART_MenuButton" => $"Panel actions for {DockTitle(button)}",
                "PART_PinButton" => $"Auto-hide or dock {DockTitle(button)}",
                "PART_MaximizeRestoreButton" => $"Maximize or restore {DockTitle(button)}",
                "PART_CloseButton" => $"Close {DockTitle(button)}",
                _ => null,
            };
            if (name is not null) SetAutomationName(button, name);
        }
        foreach (Control splitter in control.GetVisualDescendants().OfType<Control>()
                     .Where(item => item.GetType().Name == "ProportionalStackPanelSplitter"))
            SetAutomationName(splitter, "Resize adjacent workbench panels");
    }

    private static string DockTitle(Control control)
    {
        IDockable? dockable = control.GetVisualAncestors().OfType<Control>()
            .Select(item => item.DataContext).OfType<IDockable>().FirstOrDefault();
        return dockable switch
        {
            IToolDock toolDock => DockTitle(toolDock),
            { Title: { Length: > 0 } } => dockable.Title,
            _ => "workbench panel",
        };
    }

    private static string DockTitle(IToolDock dock) =>
        string.IsNullOrWhiteSpace(dock.ActiveDockable?.Title)
            ? "workbench"
            : dock.ActiveDockable.Title;

    private static void SetAutomationName(Control control, string name)
    {
        if (!string.Equals(AutomationProperties.GetName(control), name, StringComparison.Ordinal))
            AutomationProperties.SetName(control, name);
    }
}
