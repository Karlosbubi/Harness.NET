namespace Harness.Presentation.Terminal;

public interface ITerminalShell
{
    ValueTask RunAsync(CancellationToken cancellationToken = default);
}
