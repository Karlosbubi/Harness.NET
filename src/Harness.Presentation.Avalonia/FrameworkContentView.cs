using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Harness.BusinessLogic.Framework;

namespace Harness.Presentation.Avalonia;

/// <summary>
/// Presents the effective framework as scannable rules, issues, and collapsed guidance
/// documents instead of one formatted text dump. Filtering narrows rules and issues
/// together so a locked key or a validation failure can be found without reading
/// every guidance document.
/// </summary>
internal static class FrameworkContentView
{
    internal static Control Create(FrameworkSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return new TextBlock
            {
                Classes = { "muted" },
                Text = "Select an active workspace and refresh its effective framework.",
                Margin = new Thickness(4, 10),
                TextWrapping = TextWrapping.Wrap,
            };
        }

        TextBox filter = new() { PlaceholderText = "Filter rules and issues" };
        AutomationProperties.SetName(filter, "Filter framework rules and issues");

        CheckBox lockedOnly = new() { Content = "Locked only" };
        AutomationProperties.SetName(lockedOnly, "Show locked rules only");

        ContentControl host = new();
        void Refresh() => host.Content = Sections(
            snapshot,
            filter.Text ?? string.Empty,
            lockedOnly.IsChecked is true);

        // Observe the property rather than TextChanged so a programmatic filter also applies.
        filter.GetObservable(TextBox.TextProperty).Subscribe(_ => Refresh());
        lockedOnly.IsCheckedChanged += (_, _) => Refresh();

        Grid controls = new() { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 10 };
        controls.Children.Add(filter);
        lockedOnly.SetValue(Grid.ColumnProperty, 1);
        lockedOnly.VerticalAlignment = VerticalAlignment.Center;
        controls.Children.Add(lockedOnly);

        Grid root = new() { RowDefinitions = new("Auto,Auto,*"), RowSpacing = 10 };
        root.Children.Add(Validity(snapshot));
        controls.SetValue(Grid.RowProperty, 1);
        root.Children.Add(controls);
        ScrollViewer scroller = new()
        {
            Content = host,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        scroller.SetValue(Grid.RowProperty, 2);
        root.Children.Add(scroller);
        return root;
    }

    private static Control Validity(FrameworkSnapshot snapshot)
    {
        TextBlock headline = new()
        {
            Text = snapshot.IsValid ? "Framework is valid" : "Framework needs attention",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
        };
        if (!snapshot.IsValid)
        {
            headline.Classes.Add("attention");
        }

        TextBlock counts = new()
        {
            Classes = { "muted" },
            Text = $"{snapshot.Rules.Count} rule(s) · " +
                   $"{snapshot.Rules.Count(rule => rule.IsLocked)} locked · " +
                   $"{snapshot.Documents.Count} guidance document(s) · " +
                   $"{snapshot.Issues.Count} issue(s)",
            FontSize = 12,
        };
        return new StackPanel { Spacing = 2, Children = { headline, counts } };
    }

    private static Control Sections(FrameworkSnapshot snapshot, string filter, bool lockedOnly)
    {
        string trimmed = filter.Trim();
        StackPanel sections = new() { Spacing = 14 };

        IReadOnlyList<EffectiveFrameworkRule> rules =
        [
            .. snapshot.Rules.Where(rule =>
                (!lockedOnly || rule.IsLocked) && MatchesRule(rule, trimmed))
        ];
        IReadOnlyList<FrameworkIssue> issues =
        [
            .. snapshot.Issues.Where(issue => !lockedOnly && MatchesIssue(issue, trimmed))
        ];

        if (issues.Count > 0)
        {
            sections.Children.Add(Section("ISSUES", issues.Select(IssueRow)));
        }

        sections.Children.Add(rules.Count == 0
            ? Section("EFFECTIVE RULES", [Empty(trimmed.Length > 0 || lockedOnly
                ? "No rule matches the current filter."
                : "No rules are defined.")])
            : Section("EFFECTIVE RULES", rules.Select(RuleRow)));

        // Guidance documents stay collapsed: their bodies are what made the old view unreadable.
        IReadOnlyList<FrameworkDocumentView> documents =
        [
            .. snapshot.Documents.Where(document => MatchesDocument(document, trimmed))
        ];
        sections.Children.Add(documents.Count == 0
            ? Section("GUIDANCE DOCUMENTS", [Empty("No guidance document matches the current filter.")])
            : Section("GUIDANCE DOCUMENTS", documents.Select(DocumentRow)));

        return sections;
    }

    private static Control Section(string label, IEnumerable<Control> children)
    {
        StackPanel items = new() { Spacing = 6 };
        foreach (Control child in children)
        {
            items.Children.Add(child);
        }

        return new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Classes = { "eyebrow" }, Text = label },
                items,
            },
        };
    }

    private static Control Empty(string message) => new TextBlock
    {
        Classes = { "muted" },
        Text = message,
        TextWrapping = TextWrapping.Wrap,
    };

    private static Control RuleRow(EffectiveFrameworkRule rule)
    {
        StackPanel heading = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (rule.IsLocked)
        {
            heading.Children.Add(new Border
            {
                Classes = { "chip" },
                Child = new TextBlock { Text = "LOCKED" },
            });
        }

        heading.Children.Add(new TextBlock
        {
            Text = rule.Key,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });

        return new Border
        {
            Classes = { "card", "row" },
            Child = new StackPanel
            {
                Spacing = 3,
                Children =
                {
                    heading,
                    new TextBlock { Text = rule.Value, TextWrapping = TextWrapping.Wrap },
                    new TextBlock
                    {
                        Classes = { "muted" },
                        Text = $"{rule.Layer} · {rule.Source}",
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            },
        };
    }

    private static Control IssueRow(FrameworkIssue issue)
    {
        StackPanel content = new()
        {
            Spacing = 3,
            Children =
            {
                new TextBlock
                {
                    Text = $"[{issue.Code}] {issue.Message}",
                    TextWrapping = TextWrapping.Wrap,
                    FontWeight = FontWeight.SemiBold,
                },
            },
        };
        if (issue.Key is { Length: > 0 })
        {
            content.Children.Add(new TextBlock
            {
                Classes = { "muted" },
                Text = $"key: {issue.Key}",
                FontSize = 11,
            });
        }

        if (issue.Sources.Count > 0)
        {
            content.Children.Add(new TextBlock
            {
                Classes = { "muted" },
                Text = $"sources: {string.Join(", ", issue.Sources)}",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        return new Border { Classes = { "card", "row", "attention" }, Child = content };
    }

    private static Control DocumentRow(FrameworkDocumentView document) => new Expander
    {
        Header = $"{document.Source} · {document.Layer} · precedence {document.Precedence}" +
                 (document.IsPrivate ? " · private" : " · shared"),
        Content = new SelectableTextBlock
        {
            Text = document.Content,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Code,JetBrains Mono,Consolas,Menlo,monospace"),
            FontSize = 12,
            Margin = new Thickness(4, 8),
        },
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
    };

    private static bool MatchesRule(EffectiveFrameworkRule rule, string filter) =>
        filter.Length == 0 ||
        Contains(rule.Key, filter) ||
        Contains(rule.Value, filter) ||
        Contains(rule.Layer, filter) ||
        Contains(rule.Source, filter);

    private static bool MatchesIssue(FrameworkIssue issue, string filter) =>
        filter.Length == 0 ||
        Contains(issue.Code, filter) ||
        Contains(issue.Message, filter) ||
        Contains(issue.Key ?? string.Empty, filter) ||
        issue.Sources.Any(source => Contains(source, filter));

    private static bool MatchesDocument(FrameworkDocumentView document, string filter) =>
        filter.Length == 0 ||
        Contains(document.Source, filter) ||
        Contains(document.Layer, filter) ||
        Contains(document.Content, filter);

    private static bool Contains(string value, string filter) =>
        value.Contains(filter, StringComparison.OrdinalIgnoreCase);
}
