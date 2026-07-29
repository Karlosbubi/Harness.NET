using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Harness.BusinessLogic.Framework;

namespace Harness.Presentation.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class FrameworkContentViewTests
{
    private static FrameworkSnapshot Snapshot(bool isValid = true) => new(
        [
            new("global", 1, "framework.md", "Global guidance body text.", false),
            new("private-workspace", 3, "overlay", "Private overlay body text.", true),
        ],
        [
            new("style.indent", "4 spaces", "global", false, "framework.md"),
            new("tests.required", "always", "repository", true, "AGENTS.md"),
            new("logging.sink", "serilog", "private-workspace", false, "overlay"),
        ],
        isValid
            ? []
            : [new(
                "same_level_conflict",
                "Two sources define the same key.",
                "style.indent",
                ["a", "b"])]);

    private static async Task WithView(
        FrameworkSnapshot? snapshot,
        Action<Control, Window> assert)
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            Control view = FrameworkContentView.Create(snapshot);
            Window window = new() { Width = 900, Height = 700, Content = view };
            window.Show();
            window.UpdateLayout();
            assert(view, window);
            window.Close();
        }, CancellationToken.None);
    }

    private static IReadOnlyList<string> Texts(Control view) =>
    [
        .. view.GetSelfAndLogicalDescendants()
            .OfType<TextBlock>()
            .Select(block => block.Text ?? string.Empty)
    ];

    [Fact]
    public async Task Rules_are_listed_individually_rather_than_as_one_blob()
    {
        await WithView(Snapshot(), (view, _) =>
        {
            IReadOnlyList<string> texts = Texts(view);
            Assert.Contains("style.indent", texts);
            Assert.Contains("tests.required", texts);
            Assert.Contains("4 spaces", texts);

            // The old view concatenated everything into a single control.
            Assert.DoesNotContain(texts, text =>
                text.Contains("style.indent", StringComparison.Ordinal) &&
                text.Contains("tests.required", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task A_locked_rule_is_marked_without_reading_its_source_line()
    {
        await WithView(Snapshot(), (view, _) =>
            Assert.Contains("LOCKED", Texts(view)));
    }

    [Fact]
    public async Task Guidance_document_bodies_stay_collapsed()
    {
        await WithView(Snapshot(), (view, _) =>
        {
            IReadOnlyList<Expander> expanders =
                [.. view.GetLogicalDescendants().OfType<Expander>()];

            Assert.Equal(2, expanders.Count);
            Assert.All(expanders, expander => Assert.False(expander.IsExpanded));
        });
    }

    [Fact]
    public async Task Filtering_narrows_the_listed_rules()
    {
        await WithView(Snapshot(), (view, window) =>
        {
            TextBox filter = view.GetLogicalDescendants().OfType<TextBox>().First();
            filter.Text = "tests";
            window.UpdateLayout();

            IReadOnlyList<string> texts = Texts(view);
            Assert.Contains("tests.required", texts);
            Assert.DoesNotContain("style.indent", texts);
        });
    }

    [Fact]
    public async Task Filtering_to_nothing_says_so_instead_of_rendering_blank()
    {
        await WithView(Snapshot(), (view, window) =>
        {
            TextBox filter = view.GetLogicalDescendants().OfType<TextBox>().First();
            filter.Text = "no-such-key";
            window.UpdateLayout();

            Assert.Contains(Texts(view), text =>
                text.Contains("No rule matches", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task Locked_only_hides_unlocked_rules()
    {
        await WithView(Snapshot(), (view, window) =>
        {
            CheckBox lockedOnly = view.GetLogicalDescendants().OfType<CheckBox>().First();
            lockedOnly.IsChecked = true;
            window.UpdateLayout();

            IReadOnlyList<string> texts = Texts(view);
            Assert.Contains("tests.required", texts);
            Assert.DoesNotContain("style.indent", texts);
            Assert.DoesNotContain("logging.sink", texts);
        });
    }

    [Fact]
    public async Task Validation_issues_are_surfaced_above_the_rules()
    {
        await WithView(Snapshot(isValid: false), (view, _) =>
        {
            IReadOnlyList<string> texts = Texts(view);
            Assert.Contains(texts, text =>
                text.Contains("Framework needs attention", StringComparison.Ordinal));
            Assert.Contains(texts, text =>
                text.Contains("Two sources define the same key", StringComparison.Ordinal));
            Assert.Contains(texts, text => text.Contains("sources: a, b", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task A_valid_framework_reports_counts_without_an_issue_section()
    {
        await WithView(Snapshot(), (view, _) =>
        {
            IReadOnlyList<string> texts = Texts(view);
            Assert.Contains(texts, text =>
                text.Contains("3 rule(s)", StringComparison.Ordinal) &&
                text.Contains("1 locked", StringComparison.Ordinal));
            Assert.DoesNotContain("ISSUES", texts);
        });
    }

    [Fact]
    public async Task No_snapshot_explains_what_to_do_next()
    {
        await WithView(null, (view, _) =>
            Assert.Contains(Texts(view), text =>
                text.Contains("Select an active workspace", StringComparison.Ordinal)));
    }
}
