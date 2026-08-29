using Harness.BusinessLogic.Editor;
using Harness.Presentation.Avalonia.Workbench;
using Avalonia.Headless;

namespace Harness.Presentation.Avalonia.Tests;

public sealed partial class PresentationControlTests
{
    [Fact]
    public async Task Every_workbench_tool_action_has_one_palette_and_keybinding_entry()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkbenchDockHost host = CreateWorkbench(TrustedShell(), new LayoutService());

            IReadOnlyList<PaletteCommand> commands = WorkbenchPaletteCatalog.Build(
                host,
                KeybindingSettingsSnapshot.Default,
                needsTrust: null,
                needsGoal: null);
            KeybindingCommand[] expected = Enum.GetValues<KeybindingCommand>()
                .SkipWhile(command => command is not KeybindingCommand.RefreshFiles)
                .ToArray();

            Assert.Equal(expected, commands.Select(command => command.Binding!.Value));
            Assert.Equal(commands.Count, commands.Select(command => command.Id).Distinct().Count());
            PaletteCommandCatalog.RequireComplete(commands, includeWorkbenchCommands: false);
        }, CancellationToken.None);
    }
}
