using System.Security.Cryptography;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Harness.BusinessLogic.Mcp;

namespace Harness.Presentation.Avalonia;

internal sealed class AvaloniaInboundMcpUiBridge(TimeProvider timeProvider) : IInboundMcpUiBridge
{
    private MainWindow? window;

    internal void Attach(MainWindow value) => window = value;
    internal void Detach(MainWindow value)
    {
        if (ReferenceEquals(window, value)) window = null;
    }

    public async ValueTask<InboundUiSnapshot> InspectAsync(
        bool includeHarnessOwnedFrame,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MainWindow? current = window;
        if (current is null)
            return new("Harness.NET", string.Empty, Actions(false), [], [], null,
                timeProvider.GetUtcNow(),
                "The Avalonia workbench is not running.");
        return await Dispatcher.UIThread.InvokeAsync(() => Snapshot(current, includeHarnessOwnedFrame));
    }

    public async ValueTask<InboundUiActionResult> ActivateAsync(
        InboundUiActionId action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MainWindow? current = window;
        if (current is null)
            return new(action, false, "workbench_unavailable", "The Avalonia workbench is not running.");
        ValueTask<InboundUiActionResult> operation = await Dispatcher.UIThread.InvokeAsync(
            () => current.ActivateInboundUiAsync(action));
        return await operation;
    }

    public async ValueTask<InboundUiActionResult> OpenDocumentAsync(
        InboundUiDocumentRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MainWindow? current = window;
        if (current is null)
            return new(new("document.open"), false, "workbench_unavailable",
                "The Avalonia workbench is not running.");
        ValueTask<InboundUiActionResult> operation = await Dispatcher.UIThread.InvokeAsync(
            () => current.OpenInboundDocumentAsync(request));
        return await operation;
    }

    private static IReadOnlyList<InboundUiActionView> Actions(bool available) =>
    [
        new(new("chat.show"), "Show Chat", available),
        new(new("settings.open"), "Open Settings", available),
        new(new("panel.files"), "Show Files", available),
        new(new("panel.git"), "Show Git", available),
        new(new("panel.problems"), "Show Problems", available),
        new(new("panel.output"), "Show Run output", available),
    ];

    private InboundUiSnapshot Snapshot(MainWindow current, bool includeFrame)
    {
        InboundUiElementView[] elements = current.GetVisualDescendants().OfType<Control>()
            .Take(500)
            .Select((control, index) => new InboundUiElementView(
                $"element-{index}", control.GetType().Name,
                AutomationProperties.GetName(control), control.IsVisible, control.IsEnabled))
            .ToArray();
        return new("Harness.NET", current.Title ?? "Harness.NET", Actions(true), elements,
            current.InboundOpenDocuments,
            includeFrame ? RenderOwnedFrame(current) : null, timeProvider.GetUtcNow(), null);
    }

    internal static InboundRenderedFrame RenderOwnedFrame(Window current)
    {
        PixelSize size = PixelSize.FromSize(current.ClientSize, current.RenderScaling);
        if (size.Width is < 1 or > 4096 || size.Height is < 1 or > 4096)
            return new("image/png", string.Empty, size.Width, size.Height, string.Empty, true,
                "The Harness-owned frame exceeds the 4096-pixel dimension limit.");
        using RenderTargetBitmap bitmap = new(size, new(96 * current.RenderScaling,
            96 * current.RenderScaling));
        bitmap.Render(current);
        using MemoryStream stream = new();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        byte[] bytes = stream.ToArray();
        if (bytes.Length > 5_000_000)
            return new("image/png", string.Empty, size.Width, size.Height, string.Empty, true,
                "The Harness-owned frame exceeds the 5 MB encoded limit.");
        return new("image/png", Convert.ToHexStringLower(SHA256.HashData(bytes)),
            size.Width, size.Height, Convert.ToBase64String(bytes), false, null);
    }
}
