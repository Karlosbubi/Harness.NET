using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Harness.Presentation.Avalonia;

internal enum DiffViewMode
{
    Inline,
    SideBySide,
}

/// <summary>
/// Renders a bounded unified diff with decorated added/removed lines. Inline mode reviews
/// changes in place; side-by-side mode compares Git state across two aligned columns.
/// Styling is expressed as classes so an effective theme change repaints the rows.
/// </summary>
internal static class DiffContentView
{
    internal static Control Create(string diff, DiffViewMode mode = DiffViewMode.Inline)
    {
        UnifiedDiffDocument document = UnifiedDiffDocument.Parse(diff);
        Grid root = new() { RowDefinitions = new("Auto,*") };

        DiffViewMode current = mode;
        ContentControl host = new()
        {
            Content = Render(document, current),
            [Grid.RowProperty] = 1,
        };

        ToggleButton inline = ModeButton("Inline", "Review changes inline");
        ToggleButton sideBySide = ModeButton("Side by side", "Compare Git state side by side");
        inline.IsChecked = current == DiffViewMode.Inline;
        sideBySide.IsChecked = current == DiffViewMode.SideBySide;

        void Select(DiffViewMode selected)
        {
            // Keep the active mode latched; a segmented control must never clear itself.
            inline.IsChecked = selected == DiffViewMode.Inline;
            sideBySide.IsChecked = selected == DiffViewMode.SideBySide;
            if (selected != current)
            {
                current = selected;
                host.Content = Render(document, selected);
            }
        }

        inline.Click += (_, _) => Select(DiffViewMode.Inline);
        sideBySide.Click += (_, _) => Select(DiffViewMode.SideBySide);

        root.Children.Add(Toolbar(document, inline, sideBySide));
        root.Children.Add(host);
        return root;
    }

    private static Control Toolbar(UnifiedDiffDocument document, params Control[] modes)
    {
        StackPanel segments = new() { Orientation = Orientation.Horizontal, Spacing = 1 };
        foreach (Control mode in modes)
        {
            segments.Children.Add(mode);
        }

        Border group = new()
        {
            Classes = { "segmented" },
            Child = segments,
            HorizontalAlignment = HorizontalAlignment.Right,
            [Grid.ColumnProperty] = 1,
        };
        AutomationProperties.SetName(group, "Diff view mode");

        TextBlock summary = new()
        {
            Classes = { "muted" },
            Text = document.Summary,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
        };
        AutomationProperties.SetName(summary, $"Diff summary: {document.Summary}");

        Grid bar = new()
        {
            ColumnDefinitions = new("*,Auto"),
            Margin = new Thickness(10, 6),
        };
        bar.Children.Add(summary);
        bar.Children.Add(group);
        return new Border { Classes = { "diff-toolbar" }, Child = bar };
    }

    private static ToggleButton ModeButton(string text, string automationName)
    {
        ToggleButton button = new() { Classes = { "segment" }, Content = text };
        AutomationProperties.SetName(button, automationName);
        return button;
    }

    private static Control Render(UnifiedDiffDocument document, DiffViewMode mode)
    {
        if (document.IsEmpty)
        {
            return new TextBlock
            {
                Classes = { "muted" },
                Text = "This diff has no textual content.",
                Margin = new Thickness(14),
                TextWrapping = TextWrapping.Wrap,
            };
        }

        // Auto columns inside a shared-size scope keep every row aligned while still
        // measuring to the widest line, so long lines scroll instead of being clipped.
        StackPanel rows = new();
        rows.SetValue(Grid.IsSharedSizeScopeProperty, true);
        if (mode == DiffViewMode.Inline)
        {
            foreach (DiffLine line in document.Lines)
            {
                rows.Children.Add(InlineRow(line));
            }
        }
        else
        {
            foreach (DiffRow row in document.ToSideBySideRows())
            {
                rows.Children.Add(ComparisonRow(row));
            }
        }

        return new Border
        {
            Classes = { "diff-surface" },
            Child = new ScrollViewer
            {
                Content = rows,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
        };
    }

    private static Control InlineRow(DiffLine line)
    {
        Grid row = new() { ColumnDefinitions = new("Auto,Auto,Auto") };
        row.Children.Add(Gutter(line.OldLine));
        Control newGutter = Gutter(line.NewLine);
        newGutter[Grid.ColumnProperty] = 1;
        row.Children.Add(newGutter);

        Control text = LineText(line, includeMarker: true);
        text[Grid.ColumnProperty] = 2;
        row.Children.Add(text);
        return Decorate(row, line.Kind);
    }

    private static Control ComparisonRow(DiffRow row)
    {
        Grid grid = new();
        grid.ColumnDefinitions.Add(
            new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "DiffLeft" });
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(
            new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "DiffRight" });

        grid.Children.Add(Side(row.Left, isLeft: true));

        Border divider = new() { Classes = { "diff-divider" }, [Grid.ColumnProperty] = 1 };
        grid.Children.Add(divider);

        Control right = Side(row.Right, isLeft: false);
        right[Grid.ColumnProperty] = 2;
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>
    /// Renders one column of a comparison row. A null line is real absence — the other side
    /// added or removed content here — and is shown as an empty, unnumbered cell.
    /// </summary>
    private static Control Side(DiffLine? line, bool isLeft)
    {
        if (line is null)
        {
            return new Border { Classes = { "diff-line", "absent" } };
        }

        // A shared line (context, header) shows the line number belonging to its own side.
        Grid cell = new() { ColumnDefinitions = new("Auto,Auto") };
        cell.Children.Add(Gutter(isLeft ? line.OldLine : line.NewLine));
        Control text = LineText(line, includeMarker: false);
        text[Grid.ColumnProperty] = 1;
        cell.Children.Add(text);
        return Decorate(cell, line.Kind);
    }

    private static Control Gutter(int? number) => new TextBlock
    {
        Classes = { "diff-gutter" },
        Text = number?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private static Control LineText(DiffLine line, bool includeMarker)
    {
        string marker = line.Kind switch
        {
            DiffLineKind.Added => "+",
            DiffLineKind.Removed => "-",
            DiffLineKind.Context => " ",
            _ => string.Empty,
        };

        TextBlock text = new()
        {
            Classes = { "diff-text" },
            Text = includeMarker && marker.Length > 0 ? marker + line.Text : line.Text,
            TextWrapping = TextWrapping.NoWrap,
        };

        foreach (string style in TextClasses(line.Kind))
        {
            text.Classes.Add(style);
        }

        return text;
    }

    private static Control Decorate(Control content, DiffLineKind kind)
    {
        Border row = new() { Classes = { "diff-line" }, Child = content };
        if (KindClass(kind) is { } style)
        {
            row.Classes.Add(style);
        }

        return row;
    }

    private static string? KindClass(DiffLineKind kind) => kind switch
    {
        DiffLineKind.Added => "added",
        DiffLineKind.Removed => "removed",
        DiffLineKind.FileHeader => "file-header",
        DiffLineKind.HunkHeader => "hunk-header",
        _ => null,
    };

    private static IEnumerable<string> TextClasses(DiffLineKind kind) => kind switch
    {
        DiffLineKind.Added => ["added"],
        DiffLineKind.Removed => ["removed"],
        DiffLineKind.FileHeader => ["header"],
        DiffLineKind.HunkHeader => ["meta", "header"],
        DiffLineKind.Meta => ["meta"],
        _ => [],
    };
}
