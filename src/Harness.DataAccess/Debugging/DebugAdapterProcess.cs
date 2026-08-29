using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Harness.DataAccess.Debugging;

internal interface IDebugAdapterProcess : IAsyncDisposable
{
    Stream StandardInput { get; }
    Stream StandardOutput { get; }
    bool HasExited { get; }
    int? ExitCode { get; }
    string Diagnostic { get; }

    void Kill();
    ValueTask WaitForExitAsync(CancellationToken cancellationToken = default);
}

internal interface IDebugAdapterProcessFactory
{
    IDebugAdapterProcess Start(string executable, string workingDirectory);
}

internal sealed class DebugAdapterProcessFactory : IDebugAdapterProcessFactory
{
    public IDebugAdapterProcess Start(string executable, string workingDirectory) =>
        DebugAdapterProcess.Start(executable, workingDirectory);
}

internal sealed class DebugAdapterProcess : IDebugAdapterProcess
{
    private const int MaximumDiagnosticCharacters = 32 * 1024;
    private readonly Process process;
    private readonly Task diagnosticReader;
    private readonly StringBuilder diagnostic = new(MaximumDiagnosticCharacters);

    private DebugAdapterProcess(Process process)
    {
        this.process = process;
        diagnosticReader = ReadDiagnosticAsync(process.StandardError);
    }

    public Stream StandardInput => process.StandardInput.BaseStream;
    public Stream StandardOutput => process.StandardOutput.BaseStream;
    public bool HasExited => process.HasExited;
    public int? ExitCode => process.HasExited ? process.ExitCode : null;
    public string Diagnostic => diagnostic.ToString().Trim();

    internal static DebugAdapterProcess Start(string executable, string workingDirectory)
    {
        ProcessStartInfo startInfo = new(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--interpreter=vscode");
        Process process = new() { StartInfo = startInfo };
        try
        {
            if (!process.Start())
                throw new DebugAdapterRequestException("The managed debug adapter did not start.");
            return new(process);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            process.Dispose();
            throw new DebugAdapterRequestException(
                $"The managed debug adapter did not start: {exception.Message}");
        }
    }

    public void Kill()
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
    }

    public async ValueTask WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        await process.WaitForExitAsync(cancellationToken);
        await diagnosticReader.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
        await diagnosticReader;
        process.Dispose();
    }

    private async Task ReadDiagnosticAsync(StreamReader reader)
    {
        char[] buffer = new char[2048];
        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None);
            if (read == 0) return;
            int remaining = MaximumDiagnosticCharacters - diagnostic.Length;
            if (remaining > 0) diagnostic.Append(buffer, 0, Math.Min(read, remaining));
        }
    }
}
