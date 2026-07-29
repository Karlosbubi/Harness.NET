using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class DiffContentViewTests
{
    private const string SampleDiff = """
        diff --git a/src/Sample.cs b/src/Sample.cs
        @@ -10,6 +10,7 @@
             public int Value { get; }
        -    public Sample(int value)
        +    public Sample(int value, string name)
        +        Name = name;
             }
        """;

    private static IReadOnlyList<Border> Rows(Control content) =>
    [
        .. content.GetLogicalDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("diff-line"))
    ];

    private static IReadOnlyList<string> Texts(Control content) =>
    [
        .. content.GetLogicalDescendants()
            .OfType<TextBlock>()
            .Select(block => block.Text ?? string.Empty)
    ];

    [Fact]
    public async Task Added_and_removed_rows_carry_their_semantic_classes()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            Control content = DiffContentView.Create(SampleDiff);
            Window window = new() { Width = 900, Height = 600, Content = content };
            window.Show();

            IReadOnlyList<Border> rows = Rows(content);
            Assert.Equal(2, rows.Count(row => row.Classes.Contains("added")));
            Assert.Equal(1, rows.Count(row => row.Classes.Contains("removed")));
            Assert.Single(rows, row => row.Classes.Contains("file-header"));
            Assert.Single(rows, row => row.Classes.Contains("hunk-header"));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Diff_rows_resolve_the_themed_diff_colours()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            using HarnessThemeController theme = new();
            theme.Select(HarnessThemeCatalog.DarkThemeId);

            Control content = DiffContentView.Create(SampleDiff);
            Window window = new() { Width = 900, Height = 600, Content = content };
            window.Show();
            window.UpdateLayout();

            Border added = Rows(content).First(row => row.Classes.Contains("added"));
            Border removed = Rows(content).First(row => row.Classes.Contains("removed"));

            // The style system, not the view, supplies the colour; assert it actually resolved.
            Assert.Equal(
                ExpectedColour(UiThemeColorToken.DiffAddBackground),
                Assert.IsAssignableFrom<ISolidColorBrush>(added.Background).Color);
            Assert.Equal(
                ExpectedColour(UiThemeColorToken.DiffRemoveBackground),
                Assert.IsAssignableFrom<ISolidColorBrush>(removed.Background).Color);
            window.Close();
        }, CancellationToken.None);
    }

    private static Color ExpectedColour(UiThemeColorToken token) =>
        Color.Parse(HarnessThemeCatalog.BuiltIns
            .First(theme => theme.Id == HarnessThemeCatalog.DarkThemeId)
            .Colors[token]);

    [Fact]
    public async Task Inline_view_shows_change_markers_and_line_numbers()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            Control content = DiffContentView.Create(SampleDiff, DiffViewMode.Inline);
            Window window = new() { Width = 900, Height = 600, Content = content };
            window.Show();

            IReadOnlyList<string> texts = Texts(content);
            Assert.Contains(texts, text =>
                text.StartsWith("+    public Sample(int value, string name)", StringComparison.Ordinal));
            Assert.Contains(texts, text =>
                text.StartsWith("-    public Sample(int value)", StringComparison.Ordinal));
            Assert.Contains("10", texts);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Switching_to_side_by_side_drops_inline_markers()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            Control content = DiffContentView.Create(SampleDiff);
            Window window = new() { Width = 1100, Height = 600, Content = content };
            window.Show();

            ToggleButton sideBySide = content.GetLogicalDescendants()
                .OfType<ToggleButton>()
                .First(button => Equals(button.Content, "Side by side"));
            sideBySide.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();

            IReadOnlyList<string> texts = Texts(content);

            // Comparison view conveys change by column and colour, not by a +/- prefix.
            Assert.Contains(texts, text => text.Contains("string name)", StringComparison.Ordinal));
            Assert.DoesNotContain(texts, text =>
                text.StartsWith("+    public Sample", StringComparison.Ordinal));
            Assert.True(sideBySide.IsChecked);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Side_by_side_marks_absent_cells_so_columns_stay_aligned()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            Control content = DiffContentView.Create(SampleDiff, DiffViewMode.SideBySide);
            Window window = new() { Width = 1100, Height = 600, Content = content };
            window.Show();

            // One added line has no counterpart, so exactly one cell is rendered as absent.
            Assert.Single(Rows(content), row => row.Classes.Contains("absent"));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task The_active_mode_stays_latched_when_reselected()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            Control content = DiffContentView.Create(SampleDiff);
            Window window = new() { Width = 900, Height = 600, Content = content };
            window.Show();

            ToggleButton inline = content.GetLogicalDescendants()
                .OfType<ToggleButton>()
                .First(button => Equals(button.Content, "Inline"));
            Assert.True(inline.IsChecked);

            // Clicking the already-active mode must not leave the group with nothing selected.
            inline.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(inline.IsChecked);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task A_diff_without_content_states_so_honestly()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            Control content = DiffContentView.Create(string.Empty);
            Window window = new() { Width = 600, Height = 400, Content = content };
            window.Show();

            IReadOnlyList<string> texts = Texts(content);
            Assert.Contains(texts, text =>
                text.Contains("no textual content", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(texts, text =>
                text.Contains("No textual changes", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(Rows(content));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task The_summary_reports_real_counts()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            Control content = DiffContentView.Create(SampleDiff);
            Window window = new() { Width = 900, Height = 600, Content = content };
            window.Show();

            Assert.Contains(Texts(content), text =>
                text.Contains("1 file(s)", StringComparison.Ordinal) &&
                text.Contains("+2", StringComparison.Ordinal) &&
                text.Contains("−1", StringComparison.Ordinal));
            window.Close();
        }, CancellationToken.None);
    }
}
