namespace Harness.Presentation.Avalonia;

public interface IAvaloniaShell
{
    ValueTask RunAsync(CancellationToken cancellationToken = default);
}
