using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Harness.Presentation.Avalonia;

internal sealed class WorkbenchDockLayoutCodec(IFactory factory)
{
    private const int FormatVersion = 1;
    private const int MaximumNodes = 128;
    private const int MaximumDepth = 16;
    private const int MaximumFloatingWindows = 8;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        MaxDepth = MaximumDepth + 8,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    internal WorkbenchDockLayoutCaptureResult Capture(IRootDock root)
    {
        ArgumentNullException.ThrowIfNull(root);
        try
        {
            CaptureState state = new();
            LayoutNode? captured = CaptureNode(root, state, depth: 0);
            if (captured is null)
            {
                return new(null, state.Error ?? "The workbench root could not be captured.");
            }

            LayoutSnapshot snapshot = new(FormatVersion, captured);
            string? validationError = Validate(snapshot);
            return validationError is null
                ? new(JsonSerializer.Serialize(snapshot, JsonOptions), null)
                : new(null, validationError);
        }
        catch (Exception exception) when (exception is InvalidOperationException or
                                          NotSupportedException or JsonException)
        {
            return new(null, exception.Message);
        }
    }

    internal WorkbenchDockLayoutRestoreResult Restore(
        string payload,
        IReadOnlyDictionary<string, Control> contexts,
        PixelRect workingArea)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Failure("The saved workbench layout is empty.");
        }

        try
        {
            LayoutSnapshot? snapshot = JsonSerializer.Deserialize<LayoutSnapshot>(
                payload,
                JsonOptions);
            if (snapshot is null)
            {
                return Failure("The saved workbench layout could not be decoded.");
            }

            string? validationError = Validate(snapshot);
            if (validationError is not null)
            {
                return Failure(validationError);
            }

            LayoutNode normalized = NormalizeWindows(snapshot.Root, workingArea);
            IDockable restored = RestoreNode(normalized, contexts);
            if (restored is not IRootDock root)
            {
                return Failure("The saved layout did not restore a workbench root.");
            }

            IDockable? overview = Find(root, WorkbenchDockIds.OverviewDocument);
            IDocumentDock? documents = FindDocumentOwner(root);
            return overview is null || documents is null
                ? Failure("The restored layout has no usable center document region.")
                : new(root, documents, overview, null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or
                                          NotSupportedException or JsonException or ArgumentException)
        {
            return Failure(exception.Message);
        }
    }

    private LayoutNode? CaptureNode(IDockable dockable, CaptureState state, int depth)
    {
        if (state.Error is not null)
        {
            return null;
        }

        if (depth > MaximumDepth)
        {
            state.Error = "The workbench layout is nested too deeply.";
            return null;
        }

        if (!state.Visited.Add(dockable))
        {
            return null;
        }

        state.Nodes++;
        if (state.Nodes > MaximumNodes)
        {
            state.Error = "The workbench layout contains too many elements.";
            return null;
        }

        if (dockable is IDocument && dockable is not ITool &&
            !string.Equals(
                dockable.Id,
                WorkbenchDockIds.OverviewDocument,
                StringComparison.Ordinal))
        {
            return null;
        }

        if (dockable is ITool or IDocument)
        {
            if (dockable.Id is null || !WorkbenchDockIds.DurablePaneIds.Contains(dockable.Id))
            {
                state.Error = $"The workbench contains unknown pane '{dockable.Id ?? "<missing>"}'.";
                return null;
            }

            return new(
                LayoutNodeKind.Pane,
                dockable.Id,
                dockable.Title,
                SafeProportion(dockable.Proportion),
                Orientation: null,
                Alignment: null,
                IsExpanded: false,
                AutoHide: false,
                GripMode: null,
                Children: [],
                ActiveId: null,
                DefaultId: null,
                FocusedId: null,
                Hidden: [],
                LeftPinned: [],
                RightPinned: [],
                TopPinned: [],
                BottomPinned: [],
                PinnedDock: null,
                Windows: []);
        }

        LayoutNodeKind? kind = dockable switch
        {
            IRootDock => LayoutNodeKind.Root,
            IDocumentDock => LayoutNodeKind.DocumentDock,
            IToolDock => LayoutNodeKind.ToolDock,
            IProportionalDock => LayoutNodeKind.ProportionalDock,
            IProportionalDockSplitter => LayoutNodeKind.ProportionalSplitter,
            _ => null,
        };
        if (kind is null)
        {
            state.Error = $"The workbench contains unsupported Dock element '{dockable.Id}'.";
            return null;
        }

        List<LayoutNode> children = CaptureList(
            (dockable as IDock)?.VisibleDockables,
            state,
            depth + 1);
        if (state.Error is not null)
        {
            return null;
        }

        IRootDock? root = dockable as IRootDock;
        List<LayoutWindow> windows = [];
        if (root?.Windows is { } sourceWindows)
        {
            if (sourceWindows.Count > MaximumFloatingWindows)
            {
                state.Error = "The workbench contains too many floating windows.";
                return null;
            }

            foreach (IDockWindow window in sourceWindows)
            {
                if (window.Layout is null)
                {
                    continue;
                }

                LayoutNode? windowRoot = CaptureNode(window.Layout, state, depth + 1);
                if (windowRoot is not null && ContainsDurablePane(windowRoot))
                {
                    windows.Add(new(
                        window.Id,
                        window.Title,
                        window.X,
                        window.Y,
                        window.Width,
                        window.Height,
                        window.WindowState,
                        window.Topmost,
                        window.ShowInTaskbar ?? true,
                        windowRoot));
                }
            }
        }

        return new(
            kind.Value,
            dockable.Id,
            dockable.Title,
            SafeProportion(dockable.Proportion),
            (dockable as IProportionalDock)?.Orientation,
            (dockable as IToolDock)?.Alignment,
            (dockable as IToolDock)?.IsExpanded ?? false,
            (dockable as IToolDock)?.AutoHide ?? false,
            (dockable as IToolDock)?.GripMode,
            children,
            IncludedReference((dockable as IDock)?.ActiveDockable, children),
            IncludedReference((dockable as IDock)?.DefaultDockable, children),
            IncludedReference((dockable as IDock)?.FocusedDockable, children),
            CaptureList(root?.HiddenDockables, state, depth + 1),
            CaptureList(root?.LeftPinnedDockables, state, depth + 1),
            CaptureList(root?.RightPinnedDockables, state, depth + 1),
            CaptureList(root?.TopPinnedDockables, state, depth + 1),
            CaptureList(root?.BottomPinnedDockables, state, depth + 1),
            root?.PinnedDock is null ? null : CaptureNode(root.PinnedDock, state, depth + 1),
            windows);
    }

    private List<LayoutNode> CaptureList(
        IList<IDockable>? dockables,
        CaptureState state,
        int depth)
    {
        if (dockables is null)
        {
            return [];
        }

        List<LayoutNode> result = [];
        foreach (IDockable dockable in dockables)
        {
            LayoutNode? captured = CaptureNode(dockable, state, depth);
            if (captured is not null)
            {
                result.Add(captured);
            }
        }

        return result;
    }

    private IDockable RestoreNode(
        LayoutNode node,
        IReadOnlyDictionary<string, Control> contexts)
    {
        IDockable dockable = node.Kind switch
        {
            LayoutNodeKind.Root => factory.CreateRootDock(),
            LayoutNodeKind.ProportionalDock => factory.CreateProportionalDock(),
            LayoutNodeKind.ProportionalSplitter => factory.CreateProportionalDockSplitter(),
            LayoutNodeKind.ToolDock => factory.CreateToolDock(),
            LayoutNodeKind.DocumentDock => factory.CreateDocumentDock(),
            LayoutNodeKind.Pane when node.Id == WorkbenchDockIds.OverviewDocument =>
                factory.CreateDocument(),
            LayoutNodeKind.Pane => factory.CreateTool(),
            _ => throw new InvalidOperationException($"Unsupported layout node '{node.Kind}'."),
        };
        dockable.Id = node.Id ?? string.Empty;
        dockable.Title = DurableTitle(node.Id, node.Title);
        dockable.Proportion = SafeProportion(node.Proportion);
        dockable.IsCollapsable = node.Kind is not LayoutNodeKind.DocumentDock;

        if (node.Kind is LayoutNodeKind.Pane)
        {
            if (node.Id is null || !contexts.TryGetValue(node.Id, out Control? context))
            {
                throw new InvalidOperationException(
                    $"No production context is registered for pane '{node.Id}'.");
            }

            dockable.Context = context;
            bool overview = node.Id == WorkbenchDockIds.OverviewDocument;
            dockable.CanClose = !overview;
            dockable.CanFloat = !overview;
            dockable.CanPin = !overview;
            return dockable;
        }

        if (dockable is IDock dock)
        {
            IList<IDockable> children = factory.CreateList<IDockable>(
                node.Children.Select(child => RestoreNode(child, contexts)).ToArray());
            dock.VisibleDockables = children;
            dock.ActiveDockable = FindDirect(children, node.ActiveId) ?? children.FirstOrDefault();
            dock.DefaultDockable = FindDirect(children, node.DefaultId);
            dock.FocusedDockable = FindDirect(children, node.FocusedId);
            dock.CanCloseLastDockable = node.Kind is not LayoutNodeKind.DocumentDock;
        }

        if (dockable is IProportionalDock proportional)
        {
            proportional.Orientation = node.Orientation ?? Orientation.Horizontal;
        }

        if (dockable is IToolDock toolDock)
        {
            toolDock.Alignment = node.Alignment ?? Alignment.Unset;
            toolDock.IsExpanded = node.IsExpanded;
            toolDock.AutoHide = node.AutoHide;
            toolDock.GripMode = node.GripMode ?? GripMode.Visible;
        }

        if (dockable is IDocumentDock documentDock)
        {
            documentDock.CanCreateDocument = false;
        }

        if (dockable is IRootDock root)
        {
            root.HiddenDockables = RestoreList(node.Hidden, contexts);
            root.LeftPinnedDockables = RestoreList(node.LeftPinned, contexts);
            root.RightPinnedDockables = RestoreList(node.RightPinned, contexts);
            root.TopPinnedDockables = RestoreList(node.TopPinned, contexts);
            root.BottomPinnedDockables = RestoreList(node.BottomPinned, contexts);
            root.PinnedDock = node.PinnedDock is null
                ? null
                : RestoreNode(node.PinnedDock, contexts) as IToolDock;
            root.Windows = factory.CreateList<IDockWindow>(node.Windows.Select(window =>
            {
                IDockWindow restored = factory.CreateDockWindow();
                restored.Id = window.Id ?? string.Empty;
                restored.Title = window.Title;
                restored.X = window.X;
                restored.Y = window.Y;
                restored.Width = window.Width;
                restored.Height = window.Height;
                restored.WindowState = window.WindowState;
                restored.Topmost = window.Topmost;
                restored.ShowInTaskbar = window.ShowInTaskbar;
                restored.Layout = RestoreNode(window.Root, contexts) as IRootDock;
                return restored;
            }).ToArray());
        }

        return dockable;
    }

    private IList<IDockable> RestoreList(
        IReadOnlyList<LayoutNode> nodes,
        IReadOnlyDictionary<string, Control> contexts) => factory.CreateList<IDockable>(
        nodes.Select(node => RestoreNode(node, contexts)).ToArray());

    private static string? Validate(LayoutSnapshot snapshot)
    {
        if (snapshot.Version != FormatVersion)
        {
            return "The saved workbench layout uses an unsupported schema version.";
        }

        if (snapshot.Root.Kind is not LayoutNodeKind.Root ||
            snapshot.Root.Id != WorkbenchDockIds.Root)
        {
            return "The saved layout does not contain the Harness.NET workbench root.";
        }

        ValidationState state = new();
        Visit(snapshot.Root, parent: null, state, depth: 0);
        if (state.Error is not null)
        {
            return state.Error;
        }

        string? missing = WorkbenchDockIds.DurablePaneIds.FirstOrDefault(id =>
            !state.DurableCounts.TryGetValue(id, out int count) || count != 1);
        return missing is null
            ? null
            : $"The saved layout must contain exactly one '{missing}' pane; found " +
              $"[{string.Join(", ", state.DurableCounts.Keys.Order(StringComparer.Ordinal))}].";
    }

    private static void Visit(
        LayoutNode node,
        LayoutNodeKind? parent,
        ValidationState state,
        int depth)
    {
        if (state.Error is not null)
        {
            return;
        }

        if (depth > MaximumDepth || ++state.Nodes > MaximumNodes)
        {
            state.Error = depth > MaximumDepth
                ? "The saved layout is nested too deeply."
                : "The saved layout contains too many elements.";
            return;
        }

        if (node.Id is { Length: > 128 })
        {
            state.Error = "The saved layout contains an overlong identifier.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(node.Id) && !state.Identifiers.Add(node.Id))
        {
            state.Error = $"The saved layout contains duplicate id '{node.Id}'.";
            return;
        }

        if (node.Kind is LayoutNodeKind.Pane)
        {
            if (node.Id is null || !WorkbenchDockIds.DurablePaneIds.Contains(node.Id))
            {
                state.Error = $"The saved layout contains unknown pane '{node.Id ?? "<missing>"}'.";
                return;
            }

            if (node.Id == WorkbenchDockIds.OverviewDocument &&
                parent is not LayoutNodeKind.DocumentDock)
            {
                state.Error = "The workspace overview is outside a document region.";
                return;
            }

            state.DurableCounts[node.Id] =
                state.DurableCounts.GetValueOrDefault(node.Id) + 1;
        }

        if (node.Windows.Count > MaximumFloatingWindows)
        {
            state.Error = "The saved layout contains too many floating windows.";
            return;
        }

        foreach (LayoutNode child in AllChildren(node))
        {
            Visit(child, node.Kind, state, depth + 1);
        }

        foreach (LayoutWindow window in node.Windows)
        {
            Visit(window.Root, parent: null, state, depth + 1);
        }
    }

    private static IEnumerable<LayoutNode> AllChildren(LayoutNode node) =>
        node.Children
            .Concat(node.Hidden)
            .Concat(node.LeftPinned)
            .Concat(node.RightPinned)
            .Concat(node.TopPinned)
            .Concat(node.BottomPinned)
            .Concat(node.PinnedDock is null ? [] : [node.PinnedDock]);

    private static LayoutNode NormalizeWindows(LayoutNode root, PixelRect workingArea)
    {
        if (workingArea.Width < 320 || workingArea.Height < 240)
        {
            workingArea = new(0, 0, 1920, 1080);
        }

        IReadOnlyList<LayoutWindow> windows = root.Windows.Select(window =>
        {
            double width = double.IsFinite(window.Width)
                ? Math.Clamp(window.Width, 320, workingArea.Width)
                : Math.Min(800, workingArea.Width);
            double height = double.IsFinite(window.Height)
                ? Math.Clamp(window.Height, 240, workingArea.Height)
                : Math.Min(600, workingArea.Height);
            return window with
            {
                X = double.IsFinite(window.X)
                    ? Math.Clamp(window.X, workingArea.X, workingArea.Right - width)
                    : workingArea.X,
                Y = double.IsFinite(window.Y)
                    ? Math.Clamp(window.Y, workingArea.Y, workingArea.Bottom - height)
                    : workingArea.Y,
                Width = width,
                Height = height,
                Root = NormalizeWindows(window.Root, workingArea),
            };
        }).ToArray();
        return root with
        {
            Children = root.Children.Select(child => NormalizeWindows(child, workingArea)).ToArray(),
            Hidden = root.Hidden.Select(child => NormalizeWindows(child, workingArea)).ToArray(),
            LeftPinned = root.LeftPinned.Select(child => NormalizeWindows(child, workingArea)).ToArray(),
            RightPinned = root.RightPinned.Select(child => NormalizeWindows(child, workingArea)).ToArray(),
            TopPinned = root.TopPinned.Select(child => NormalizeWindows(child, workingArea)).ToArray(),
            BottomPinned = root.BottomPinned.Select(child => NormalizeWindows(child, workingArea)).ToArray(),
            PinnedDock = root.PinnedDock is null
                ? null
                : NormalizeWindows(root.PinnedDock, workingArea),
            Windows = windows,
        };
    }

    private static IDockable? Find(IDockable root, string id)
    {
        HashSet<IDockable> visited = new(ReferenceEqualityComparer.Instance);
        Stack<IDockable> pending = new();
        pending.Push(root);
        while (pending.TryPop(out IDockable? current))
        {
            if (!visited.Add(current))
            {
                continue;
            }

            if (current.Id == id)
            {
                return current;
            }

            if (current is IDock dock)
            {
                foreach (IDockable child in dock.VisibleDockables ?? [])
                {
                    pending.Push(child);
                }
            }

            if (current is IRootDock nestedRoot)
            {
                foreach (IDockable child in RootSpecialDockables(nestedRoot))
                {
                    pending.Push(child);
                }

                foreach (IDockWindow window in nestedRoot.Windows ?? [])
                {
                    if (window.Layout is not null)
                    {
                        pending.Push(window.Layout);
                    }
                }
            }
        }

        return null;
    }

    private static IDocumentDock? FindDocumentOwner(IRootDock root)
    {
        IDockable? overview = Find(root, WorkbenchDockIds.OverviewDocument);
        return overview?.Owner as IDocumentDock ?? FindDock(root);

        static IDocumentDock? FindDock(IDockable node)
        {
            if (node is IDocumentDock documentDock &&
                documentDock.VisibleDockables?.Any(item =>
                    item.Id == WorkbenchDockIds.OverviewDocument) is true)
            {
                return documentDock;
            }

            if (node is IDock dock)
            {
                foreach (IDockable child in dock.VisibleDockables ?? [])
                {
                    IDocumentDock? found = FindDock(child);
                    if (found is not null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }
    }

    private static IEnumerable<IDockable> RootSpecialDockables(IRootDock root) =>
        (root.HiddenDockables ?? [])
            .Concat(root.LeftPinnedDockables ?? [])
            .Concat(root.RightPinnedDockables ?? [])
            .Concat(root.TopPinnedDockables ?? [])
            .Concat(root.BottomPinnedDockables ?? [])
            .Concat(root.PinnedDock is null ? [] : [root.PinnedDock]);

    private static bool ContainsDurablePane(LayoutNode node) =>
        node.Kind is LayoutNodeKind.Pane ||
        AllChildren(node).Any(ContainsDurablePane) ||
        node.Windows.Any(window => ContainsDurablePane(window.Root));

    private static string? IncludedReference(IDockable? candidate, IReadOnlyList<LayoutNode> children) =>
        candidate?.Id is { } id && children.Any(child => child.Id == id) ? id : null;

    private static IDockable? FindDirect(IList<IDockable> dockables, string? id) =>
        id is null ? null : dockables.FirstOrDefault(item => item.Id == id);

    private static double SafeProportion(double value) =>
        double.IsFinite(value) && value > 0.02 && value < 0.98 ? value : 0.5;

    private static string DurableTitle(string? id, string stored) => id switch
    {
        WorkbenchDockIds.NavigationTool => "Workspace",
        WorkbenchDockIds.FilesTool => "Files",
        WorkbenchDockIds.ContextTool => "Goal context",
        WorkbenchDockIds.GitTool => "Git",
        WorkbenchDockIds.ConversationTool => "Conversation",
        WorkbenchDockIds.OverviewDocument => "Workspace overview",
        _ => stored.Length <= 128 ? stored : string.Empty,
    };

    private static WorkbenchDockLayoutRestoreResult Failure(string error) =>
        new(null, null, null, error);

    private sealed class CaptureState
    {
        internal HashSet<IDockable> Visited { get; } = new(ReferenceEqualityComparer.Instance);
        internal int Nodes { get; set; }
        internal string? Error { get; set; }
    }

    private sealed class ValidationState
    {
        internal HashSet<string> Identifiers { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, int> DurableCounts { get; } = new(StringComparer.Ordinal);
        internal int Nodes { get; set; }
        internal string? Error { get; set; }
    }

    private sealed record LayoutSnapshot(int Version, LayoutNode Root);

    private sealed record LayoutNode(
        LayoutNodeKind Kind,
        string? Id,
        string Title,
        double Proportion,
        Orientation? Orientation,
        Alignment? Alignment,
        bool IsExpanded,
        bool AutoHide,
        GripMode? GripMode,
        IReadOnlyList<LayoutNode> Children,
        string? ActiveId,
        string? DefaultId,
        string? FocusedId,
        IReadOnlyList<LayoutNode> Hidden,
        IReadOnlyList<LayoutNode> LeftPinned,
        IReadOnlyList<LayoutNode> RightPinned,
        IReadOnlyList<LayoutNode> TopPinned,
        IReadOnlyList<LayoutNode> BottomPinned,
        LayoutNode? PinnedDock,
        IReadOnlyList<LayoutWindow> Windows);

    private sealed record LayoutWindow(
        string? Id,
        string Title,
        double X,
        double Y,
        double Width,
        double Height,
        DockWindowState WindowState,
        bool Topmost,
        bool ShowInTaskbar,
        LayoutNode Root);

    private enum LayoutNodeKind
    {
        Root,
        ProportionalDock,
        ProportionalSplitter,
        ToolDock,
        DocumentDock,
        Pane,
    }
}

internal sealed record WorkbenchDockLayoutCaptureResult(string? Payload, string? Error);

internal sealed record WorkbenchDockLayoutRestoreResult(
    IRootDock? Layout,
    IDocumentDock? Documents,
    IDockable? Overview,
    string? Error);
