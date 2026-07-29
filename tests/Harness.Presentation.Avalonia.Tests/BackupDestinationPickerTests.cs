using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

namespace Harness.Presentation.Avalonia.Tests;

[Collection("Avalonia UI")]
public sealed class BackupDestinationPickerTests
{
    private sealed class StubPicker(BackupFilePickerResult result) : IBackupFilePicker
    {
        internal int Calls { get; private set; }

        public ValueTask<BackupFilePickerResult> PickAsync(
            TopLevel owner,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(result);
        }
    }

    private static async Task WithDialog(
        StubPicker picker,
        Action<OperationsDialog, TextBox, Button> assert)
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            OperationsDialog dialog = new(
                AvaloniaPresentationStoreTests.CreateStore(),
                CancellationToken.None,
                picker);
            dialog.Show();

            TextBox path = dialog.GetLogicalDescendants()
                .OfType<TextBox>()
                .First(box => !box.IsReadOnly);
            Button choose = dialog.GetLogicalDescendants()
                .OfType<Button>()
                .First(button => Equals(button.Content, "Choose…"));

            assert(dialog, path, choose);
            dialog.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Choosing_a_destination_fills_the_path_without_typing()
    {
        string destination = Path.Combine(Path.GetTempPath(), $"harness-{Guid.NewGuid():N}.zip");
        StubPicker picker = new(new(new(destination), null));

        await WithDialog(picker, (_, path, choose) =>
        {
            choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, picker.Calls);
            Assert.Equal(destination, path.Text);
        });
    }

    [Fact]
    public async Task Cancelling_the_picker_leaves_the_path_untouched()
    {
        StubPicker picker = new(new(null, null));

        await WithDialog(picker, (_, path, choose) =>
        {
            path.Text = "/existing/typed/path.zip";
            choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("/existing/typed/path.zip", path.Text);
        });
    }

    [Fact]
    public async Task An_unavailable_picker_keeps_manual_entry_available()
    {
        StubPicker picker = new(new(null, "This desktop does not provide a save dialog."));

        await WithDialog(picker, (dialog, path, choose) =>
        {
            choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Contains(
                dialog.GetLogicalDescendants().OfType<TextBlock>(),
                block => (block.Text ?? string.Empty)
                    .Contains("does not provide a save dialog", StringComparison.Ordinal));
            Assert.False(path.IsReadOnly);
        });
    }
}
