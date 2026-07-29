using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;

namespace Harness.Presentation.Avalonia.Tests;

public sealed class CommandPaletteFilterTests
{
    private static PaletteCommand Command(
        string category,
        string title,
        string? unavailable = null) =>
        new($"{category}.{title}", category, title, () => ValueTask.CompletedTask,
            UnavailableReason: unavailable);

    private static readonly IReadOnlyList<PaletteCommand> Commands =
    [
        Command("Git", "Open working-tree diff"),
        Command("Panels", "Show Files panel"),
        Command("Panels", "Show Git panel"),
        Command("Layout", "Reset workbench layout"),
        Command("Workspace", "Open workspace…"),
    ];

    [Fact]
    public void An_empty_query_keeps_every_command()
    {
        Assert.Equal(Commands.Count, CommandPaletteFilter.Rank(Commands, string.Empty).Count);
        Assert.Equal(Commands.Count, CommandPaletteFilter.Rank(Commands, "   ").Count);
    }

    [Fact]
    public void Matching_is_a_case_insensitive_subsequence()
    {
        IReadOnlyList<PaletteCommand> ranked = CommandPaletteFilter.Rank(Commands, "gitdiff");

        Assert.Equal("Open working-tree diff", ranked[0].Title);
    }

    [Fact]
    public void A_query_that_matches_nothing_returns_nothing()
    {
        Assert.Empty(CommandPaletteFilter.Rank(Commands, "zzzz"));
    }

    [Fact]
    public void Word_start_matches_outrank_scattered_ones()
    {
        Assert.True(
            CommandPaletteFilter.Score("Panels: Show Files panel", "sfp") >
            CommandPaletteFilter.Score("Panels: Show Files panel", "sel"));
    }

    [Fact]
    public void Unavailable_commands_rank_below_available_ones()
    {
        IReadOnlyList<PaletteCommand> commands =
        [
            Command("Git", "Open working-tree diff", "Trust the workspace first"),
            Command("Git", "Show Git panel"),
        ];

        IReadOnlyList<PaletteCommand> ranked = CommandPaletteFilter.Rank(commands, "git");

        Assert.True(ranked[0].IsAvailable);
        Assert.False(ranked[^1].IsAvailable);
    }
}

[Collection("Avalonia UI")]
public sealed class CommandPaletteDialogTests
{
    private static PaletteCommand Command(
        string title,
        Action? onInvoke = null,
        string? unavailable = null) =>
        new(title, "Test", title,
            () => { onInvoke?.Invoke(); return ValueTask.CompletedTask; },
            UnavailableReason: unavailable);

    private static async Task WithPalette(
        IReadOnlyList<PaletteCommand> commands,
        Action<CommandPaletteDialog, TextBox, ListBox> assert)
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            CommandPaletteDialog palette = new(commands);
            palette.Show();
            palette.UpdateLayout();

            TextBox query = palette.GetLogicalDescendants().OfType<TextBox>().First();
            ListBox results = palette.GetLogicalDescendants().OfType<ListBox>().First();
            assert(palette, query, results);
            palette.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Every_command_is_listed_before_typing()
    {
        IReadOnlyList<PaletteCommand> commands = [Command("Alpha"), Command("Beta")];

        await WithPalette(commands, (_, _, results) => Assert.Equal(2, results.ItemCount));
    }

    [Fact]
    public async Task Typing_narrows_and_preselects_the_best_match()
    {
        IReadOnlyList<PaletteCommand> commands = [Command("Alpha"), Command("Beta")];

        await WithPalette(commands, (palette, query, results) =>
        {
            query.Text = "bet";
            palette.UpdateLayout();

            Assert.Equal(1, results.ItemCount);
            Assert.Equal("Beta", Assert.IsType<PaletteCommand>(results.SelectedItem).Title);
        });
    }

    [Fact]
    public async Task An_unavailable_command_is_never_preselected()
    {
        IReadOnlyList<PaletteCommand> commands =
        [
            Command("Blocked", unavailable: "Trust the workspace first"),
            Command("Ready"),
        ];

        await WithPalette(commands, (_, _, results) =>
            Assert.Equal("Ready", Assert.IsType<PaletteCommand>(results.SelectedItem).Title));
    }

    [Fact]
    public async Task An_unavailable_command_states_its_reason_instead_of_hiding()
    {
        IReadOnlyList<PaletteCommand> commands =
        [
            Command("Blocked", unavailable: "Trust the workspace first"),
        ];

        await WithPalette(commands, (palette, _, _) =>
            Assert.Contains(
                palette.GetLogicalDescendants().OfType<TextBlock>(),
                block => (block.Text ?? string.Empty)
                    .Contains("Trust the workspace first", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Arrow_keys_move_the_selection_while_typing()
    {
        IReadOnlyList<PaletteCommand> commands = [Command("Alpha"), Command("Beta")];

        await WithPalette(commands, (palette, query, results) =>
        {
            Assert.Equal(0, results.SelectedIndex);
            query.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Down,
            });
            Assert.Equal(1, results.SelectedIndex);

            // Selection wraps so the list is always reachable from either end.
            query.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Down,
            });
            Assert.Equal(0, results.SelectedIndex);
        });
    }

    [Fact]
    public async Task Escape_dismisses_without_running_anything()
    {
        bool invoked = false;
        IReadOnlyList<PaletteCommand> commands = [Command("Alpha", () => invoked = true)];

        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            CommandPaletteDialog palette = new(commands);
            palette.Show();
            palette.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape,
            });

            Assert.False(invoked);
        }, CancellationToken.None);
    }
}
