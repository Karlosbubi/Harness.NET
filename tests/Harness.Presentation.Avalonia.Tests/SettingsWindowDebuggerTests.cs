using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Harness.BusinessLogic.Debugging;

namespace Harness.Presentation.Avalonia.Tests;

public sealed partial class PresentationControlTests
{
    [Fact]
    public async Task Debugger_settings_expose_verified_install_status_and_actions()
    {
        DebuggerSettingsService debugger = new();
        using AvaloniaPresentationStore store = AvaloniaPresentationStoreTests.CreateStore(
            developerDebuggerSettingsService: debugger);
        await store.LoadAsync(CancellationToken.None);
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            SettingsWindow window = new(store, CancellationToken.None);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            ListBox categories = Assert.Single(window.GetLogicalDescendants().OfType<ListBox>());
            categories.SelectedItem = SettingsCatalog.All.Single(category =>
                category.Id is SettingsCategoryId.Debugger);
            Dispatcher.UIThread.RunJobs();

            string?[] names = window.GetLogicalDescendants().OfType<Control>()
                .Select(AutomationProperties.GetName).ToArray();
            Assert.Contains("Install or repair verified .NET debugger", names);
            Assert.Contains("Verify .NET debugger integrity", names);
            Assert.Contains("Remove managed .NET debugger", names);
            Assert.Contains("NetCoreDbg 3.2.0-1092", string.Join('\n', window
                .GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text)),
                StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    private sealed class DebuggerSettingsService : IDeveloperDebuggerSettingsService
    {
        private static readonly DebugAdapterStatus Status = new(
            DebugAdapterAvailability.NotInstalled,
            new("3.2.0-1092"),
            new("linux-x64"),
            "The managed debugger is not installed.",
            CanInstall: true,
            CanRemove: false);

        public ValueTask<DebugAdapterStatus> GetAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Status);

        public ValueTask<DebugAdapterStatus> InstallAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Status);

        public ValueTask<DebugAdapterStatus> RemoveAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(Status);
    }
}
