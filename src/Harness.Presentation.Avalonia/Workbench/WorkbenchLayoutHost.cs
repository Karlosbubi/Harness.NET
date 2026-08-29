using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Dock.Avalonia.Controls;
using Dock.Model.Avalonia;
using Dock.Model.Controls;
using Dock.Model.Core;
using Harness.BusinessLogic.Documents;
using Harness.BusinessLogic.Layouts;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed class WorkbenchLayoutHost
{
    private readonly IWorkbenchLayoutService service;
    private readonly Factory factory;
    private readonly WorkbenchDockLayoutCodec codec;
    private readonly IReadOnlyDictionary<string, Control> durableContexts;
    private readonly Func<WorkbenchDocumentTransition, ValueTask<bool>> closeDocuments;
    private readonly Action<IDocumentDock, IDockable> replaceDocuments;
    private readonly DockControl control;
    private readonly CancellationToken cancellationToken;
    private readonly string defaultPayload;
    private bool adaptiveLeftCollapsed;
    private bool adaptiveRightCollapsed;
    private bool adaptiveBottomCollapsed;
    private double expandedLeftProportion = 0.19;
    private double expandedRightProportion = 0.22;
    private double expandedBottomProportion = 0.45;
    private bool viewportInitialized;

    internal WorkbenchLayoutHost(
        IWorkbenchLayoutService service,
        Factory factory,
        IRootDock root,
        IDocumentDock documents,
        IDockable overview,
        IToolDock left,
        IToolDock right,
        IToolDock bottom,
        IReadOnlyDictionary<string, Control> durableContexts,
        Func<WorkbenchDocumentTransition, ValueTask<bool>> closeDocuments,
        Action<IDocumentDock, IDockable> replaceDocuments,
        DockControl control,
        CancellationToken cancellationToken)
    {
        this.service = service;
        this.factory = factory;
        this.durableContexts = durableContexts;
        this.closeDocuments = closeDocuments;
        this.replaceDocuments = replaceDocuments;
        this.control = control;
        this.cancellationToken = cancellationToken;
        Root = root;
        Documents = documents;
        Overview = overview;
        Left = left;
        Right = right;
        Bottom = bottom;
        codec = new(factory);
        WorkbenchDockLayoutCaptureResult captured = codec.Capture(root);
        defaultPayload = captured.Payload ?? throw new InvalidOperationException(
            $"Dock did not create a valid default layout: {captured.Error}");
        Actions = BuildActions();
    }

    internal IRootDock Root { get; private set; }
    internal IDocumentDock Documents { get; private set; }
    internal IDockable Overview { get; private set; }
    internal IToolDock Left { get; private set; }
    internal IToolDock Right { get; private set; }
    internal IToolDock Bottom { get; private set; }
    internal TextBlock Status { get; } = new()
    {
        MaxWidth = 180,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };
    internal Control Actions { get; }
    internal bool IsCompactViewport { get; private set; }

    internal async ValueTask RestoreAsync()
    {
        WorkbenchLayoutLoadResult stored = await service.LoadAsync(cancellationToken);
        if (stored.State is WorkbenchLayoutLoadState.Missing)
        {
            Status.Text = "Default layout";
            Status.IsVisible = false;
            return;
        }
        if (stored.Layout is null)
        {
            Status.Text = $"Saved layout rejected · {stored.Error ?? "invalid private state"}";
            Status.IsVisible = true;
            return;
        }
        WorkbenchDockLayoutRestoreResult restored = codec.Restore(
            stored.Layout.Value, durableContexts, WorkingArea());
        if (restored.Layout is null || restored.Documents is null || restored.Overview is null)
        {
            Status.Text = $"Saved layout rejected · {restored.Error ?? "invalid Dock graph"}";
            Status.IsVisible = true;
            return;
        }
        Apply(restored.Layout, restored.Documents, restored.Overview);
        Status.Text = "Layout restored";
        Status.IsVisible = true;
    }

    internal async ValueTask SaveAsync(CancellationToken saveCancellationToken = default)
    {
        WorkbenchDockLayoutCaptureResult captured = codec.Capture(Root);
        if (captured.Payload is null)
        {
            Status.Text = $"Layout not saved · {captured.Error ?? "invalid Dock graph"}";
            Status.IsVisible = true;
            return;
        }
        WorkbenchLayoutWriteResult result = await service.SaveAsync(
            new(captured.Payload), saveCancellationToken);
        Status.Text = result.Succeeded
            ? "Layout saved"
            : $"Layout not saved · {result.Error ?? "private state unavailable"}";
        Status.IsVisible = true;
    }

    internal async ValueTask ResetAsync()
    {
        if (!await closeDocuments(WorkbenchDocumentTransition.Close))
        {
            Status.Text = "Layout reset cancelled · unsaved source changes kept";
            return;
        }
        WorkbenchLayoutWriteResult reset = await service.ResetAsync(cancellationToken);
        WorkbenchDockLayoutRestoreResult restored = codec.Restore(
            defaultPayload, durableContexts, WorkingArea());
        if (restored.Layout is null || restored.Documents is null || restored.Overview is null)
        {
            Status.Text = $"Layout reset failed · {restored.Error ?? "invalid default layout"}";
            return;
        }
        Apply(restored.Layout, restored.Documents, restored.Overview);
        Status.Text = reset.Succeeded
            ? "Default layout restored"
            : $"Default active; stored layout not removed · {reset.Error}";
        Status.IsVisible = true;
    }

    internal void ApplyViewport(double width, double height)
    {
        bool compact = width > 0 && width < 1024;
        bool narrow = width > 0 && width < 840;
        bool shortViewport = height > 0 && height < 560;
        IsCompactViewport = compact || shortViewport;
        if (!viewportInitialized && width > 0 && height > 0)
        {
            Left.IsExpanded = !narrow;
            Right.IsExpanded = !compact;
            Bottom.IsExpanded = !shortViewport;
            if (narrow) CollapseInitial(Left, 0.06, constrainWidth: true);
            if (compact) CollapseInitial(Right, 0.06, constrainWidth: true);
            if (shortViewport) CollapseInitial(Bottom, 0.08, constrainWidth: false);
            adaptiveLeftCollapsed = narrow;
            adaptiveRightCollapsed = compact;
            adaptiveBottomCollapsed = shortViewport;
            viewportInitialized = true;
            return;
        }
        SetAdaptiveExpansion(Left, narrow, 0.06, true,
            ref adaptiveLeftCollapsed, ref expandedLeftProportion);
        SetAdaptiveExpansion(Right, compact, 0.06, true,
            ref adaptiveRightCollapsed, ref expandedRightProportion);
        SetAdaptiveExpansion(Bottom, shortViewport, 0.08, false,
            ref adaptiveBottomCollapsed, ref expandedBottomProportion);
    }

    internal IToolDock? DefaultToolDock(string id) => id switch
    {
        WorkbenchDockIds.NavigationTool or WorkbenchDockIds.FilesTool => Left,
        WorkbenchDockIds.ContextTool or WorkbenchDockIds.GitTool => Right,
        WorkbenchDockIds.ConversationTool or WorkbenchDockIds.RunOutputTool or
            WorkbenchDockIds.ProblemsTool => Bottom,
        _ => null,
    };

    internal void RemoveFromSpecialCollections(IDockable tool)
    {
        Root.HiddenDockables?.Remove(tool);
        Root.LeftPinnedDockables?.Remove(tool);
        Root.RightPinnedDockables?.Remove(tool);
        Root.TopPinnedDockables?.Remove(tool);
        Root.BottomPinnedDockables?.Remove(tool);
    }

    internal void RestoreAdaptiveProportion(IToolDock owner)
    {
        if (ReferenceEquals(owner, Left) && adaptiveLeftCollapsed)
        {
            owner.Proportion = expandedLeftProportion;
            owner.MaxWidth = double.PositiveInfinity;
            adaptiveLeftCollapsed = false;
        }
        else if (ReferenceEquals(owner, Right) && adaptiveRightCollapsed)
        {
            owner.Proportion = expandedRightProportion;
            owner.MaxWidth = double.PositiveInfinity;
            adaptiveRightCollapsed = false;
        }
        else if (ReferenceEquals(owner, Bottom) && adaptiveBottomCollapsed)
        {
            owner.Proportion = expandedBottomProportion;
            owner.MaxHeight = double.PositiveInfinity;
            adaptiveBottomCollapsed = false;
        }
        SetDockContentVisibility(owner, visible: true);
    }

    internal T Find<T>(string id) where T : class, IDockable =>
        FindDockable<T>(Root, id);

    internal IDockable? Find(string id) => FindDockable(Root, id);

    internal static void EnsureDefaultTools(
        IToolDock left, IToolDock right, IToolDock bottom, string stage)
    {
        if (left.VisibleDockables?.Count != 2 || right.VisibleDockables?.Count != 2 ||
            bottom.VisibleDockables?.Count != 3)
            throw new InvalidOperationException($"Dock lost the default tool panels {stage}.");
    }

    private Control BuildActions()
    {
        Button save = new() { Content = "↓" };
        AutomationProperties.SetName(save, "Save current panel layout");
        ToolTip.SetTip(save, "Save panel layout");
        save.Classes.Add("icon");
        save.Click += async (_, _) => await SaveAsync(cancellationToken);
        Button reset = new() { Content = "↺" };
        AutomationProperties.SetName(reset, "Reset panels to the default layout");
        ToolTip.SetTip(reset, "Reset panel layout");
        reset.Classes.Add("icon");
        reset.Click += async (_, _) => await ResetAsync();
        AutomationProperties.SetName(Status, "Workbench layout status");
        Status.Text = "Default layout";
        Status.IsVisible = false;
        return new StackPanel
        {
            Orientation = global::Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { save, reset },
        };
    }

    private void Apply(IRootDock restored, IDocumentDock restoredDocuments, IDockable restoredOverview)
    {
        Root.ExitWindows?.Execute(null);
        foreach (Control context in durableContexts.Values)
            WorkbenchDockContent.ReleaseFromPresenter(context);
        foreach ((string id, Control context) in durableContexts)
            if (FindDockable(restored, id) is { } dockable)
                WorkbenchDockContent.Attach(dockable, context);
        control.Layout = null;
        Root = restored;
        Documents = restoredDocuments;
        Overview = restoredOverview;
        Left = FindDockable<IToolDock>(Root, WorkbenchDockIds.Left);
        Right = FindDockable<IToolDock>(Root, WorkbenchDockIds.Right);
        Bottom = FindDockable<IToolDock>(Root, WorkbenchDockIds.Bottom);
        SetDockContentVisibility(Left, true);
        SetDockContentVisibility(Right, true);
        SetDockContentVisibility(Bottom, true);
        adaptiveLeftCollapsed = false;
        adaptiveRightCollapsed = false;
        adaptiveBottomCollapsed = false;
        factory.InitLayout(Root);
        control.Layout = Root;
        Root.ShowWindows?.Execute(null);
        IDockable restoredActive = Documents.ActiveDockable ?? Overview;
        factory.SetActiveDockable(restoredActive);
        replaceDocuments(Documents, Overview);
        viewportInitialized = true;
        ApplyViewport(control.Bounds.Width, control.Bounds.Height);
    }

    private void CollapseInitial(IToolDock dock, double proportion, bool constrainWidth)
    {
        dock.Proportion = proportion;
        dock.CollapsedProportion = proportion;
        if (constrainWidth) dock.MaxWidth = 76;
        else dock.MaxHeight = 84;
        SetDockContentVisibility(dock, false);
    }

    private static void SetAdaptiveExpansion(
        IToolDock dock,
        bool collapse,
        double collapsedProportion,
        bool constrainWidth,
        ref bool adaptivelyCollapsed,
        ref double expandedProportion)
    {
        if (collapse && !adaptivelyCollapsed)
        {
            if (double.IsFinite(dock.Proportion) && dock.Proportion > 0)
                expandedProportion = dock.Proportion;
            dock.Proportion = collapsedProportion;
            dock.CollapsedProportion = collapsedProportion;
            if (constrainWidth) dock.MaxWidth = 76;
            else dock.MaxHeight = 84;
            SetDockContentVisibility(dock, false);
            dock.IsExpanded = false;
            adaptivelyCollapsed = true;
        }
        else if (!collapse && adaptivelyCollapsed)
        {
            dock.Proportion = expandedProportion;
            dock.CollapsedProportion = expandedProportion;
            dock.MaxWidth = double.PositiveInfinity;
            dock.MaxHeight = double.PositiveInfinity;
            SetDockContentVisibility(dock, true);
            dock.IsExpanded = true;
            adaptivelyCollapsed = false;
        }
    }

    private PixelRect WorkingArea()
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(control);
        return topLevel?.Screens?.ScreenFromVisual(control)?.WorkingArea ??
               new PixelRect(0, 0, 1920, 1080);
    }

    private static void SetDockContentVisibility(IToolDock dock, bool visible)
    {
        if (dock.VisibleDockables is null) return;
        foreach (IDockable dockable in dock.VisibleDockables)
            if (dockable.Context is Control control) control.IsVisible = visible;
    }

    private static T FindDockable<T>(IDockable root, string id) where T : class, IDockable =>
        FindDockable(root, id) as T ?? throw new InvalidOperationException(
            $"Restored Dock layout is missing required node '{id}'.");

    private static IDockable? FindDockable(IDockable root, string id)
    {
        if (string.Equals(root.Id, id, StringComparison.Ordinal)) return root;
        if (root is IDock dock)
        {
            foreach (IDockable child in dock.VisibleDockables ?? [])
                if (FindDockable(child, id) is { } found) return found;
        }
        if (root is IRootDock rootDock)
        {
            foreach (IDockable child in rootDock.HiddenDockables ?? [])
                if (FindDockable(child, id) is { } found) return found;
            foreach (IDockable child in rootDock.LeftPinnedDockables ?? [])
                if (FindDockable(child, id) is { } found) return found;
            foreach (IDockable child in rootDock.RightPinnedDockables ?? [])
                if (FindDockable(child, id) is { } found) return found;
            foreach (IDockable child in rootDock.TopPinnedDockables ?? [])
                if (FindDockable(child, id) is { } found) return found;
            foreach (IDockable child in rootDock.BottomPinnedDockables ?? [])
                if (FindDockable(child, id) is { } found) return found;
        }
        return null;
    }
}
