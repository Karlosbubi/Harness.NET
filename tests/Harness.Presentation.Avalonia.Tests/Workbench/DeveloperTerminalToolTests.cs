using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Harness.BusinessLogic.Privacy;
using Harness.BusinessLogic.Terminal;
using Harness.BusinessLogic.Workspaces;
using Harness.Presentation.Avalonia.Workbench;

namespace Harness.Presentation.Avalonia.Tests;

public sealed partial class PresentationControlTests
{
    [Fact]
    public async Task Terminal_tool_renders_typed_context_and_forwards_emulated_io()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            TerminalService terminal = new();
            SensitiveGuard guard = new();
            DeveloperTerminalTool tool = new(
                terminal, TrustedShell, CancellationToken.None, guard);
            Window window = new() { Content = tool.Content, Width = 900, Height = 500 };
            window.Show();

            tool.CreateAsync().AsTask().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(tool.ActiveTerminal);
            Assert.Contains("Original workspace", tool.Metadata.Text, StringComparison.Ordinal);
            Assert.Contains("Working directory .", tool.Metadata.Text, StringComparison.Ordinal);
            Assert.Contains("Trusted: yes", tool.Metadata.Text, StringComparison.Ordinal);
            Assert.Contains("Transient", tool.Metadata.Text, StringComparison.Ordinal);
            Assert.True(tool.StopButton.IsEnabled);
            Assert.True(guard.IsSensitive);
            Assert.Equal(1, tool.ActiveTerminal!.Search("Grüße"));

            tool.ActiveTerminal.Model!.Send(Encoding.UTF8.GetBytes("typed input"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("typed input", Encoding.UTF8.GetString(terminal.Written));

            tool.StopAsync().AsTask().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.False(tool.StopButton.IsEnabled);
            Assert.Contains("stopped", tool.Status.Text!, StringComparison.OrdinalIgnoreCase);
            tool.CloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(guard.IsSensitive);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Terminal_is_a_durable_dockable_bottom_tool()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            WorkbenchDockHost workbench = CreateWorkbench(
                TrustedShell(), new(), developerTerminal: new TerminalService());
            Window window = new() { Content = workbench.Control };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.True(workbench.ShowTerminal());
            Assert.NotNull(Find<Dock.Model.Controls.ITool>(
                workbench.Root,
                WorkbenchDockIds.TerminalTool));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Terminal_restores_only_expired_lifecycle_metadata_after_restart()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(RenderingTestAppBuilder));
        await session.Dispatch(() =>
        {
            TerminalService terminal = new();
            terminal.SeedInterrupted();
            SensitiveGuard guard = new();
            DeveloperTerminalTool tool = new(
                terminal, TrustedShell, CancellationToken.None, guard);
            Window window = new() { Content = tool.Content, Width = 900, Height = 500 };
            window.Show();

            tool.Update(TrustedShell());
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(tool.ActiveTerminal);
            Assert.Contains("expired", tool.Metadata.Text, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, tool.ActiveTerminal!.Search("No process was restored"));
            Assert.False(tool.StopButton.IsEnabled);
            Assert.False(guard.IsSensitive);
            Assert.Equal(0, terminal.StartCalls);
            window.Close();
        }, CancellationToken.None);
    }

    private sealed class TerminalService : IDeveloperTerminalService
    {
        private readonly TaskCompletionSource stopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool outputRead;
        private DeveloperTerminalSessionView? current;

        public byte[] Written { get; private set; } = [];
        public int StartCalls { get; private set; }

        public void SeedInterrupted()
        {
            current = new(
                new("terminal-before-restart"), new("workspace-1"),
                new(new("workspace-1"), null, new("main"),
                    WorkbenchWorkspaceScope.OriginalWorkspace,
                    "Original workspace · user-editable source context"),
                new("."), new("bash"),
                new("Inherited host environment with locked terminal policy"),
                new("Transient · content expired after restart"), new(100, 30),
                DeveloperTerminalSessionState.Interrupted,
                DateTimeOffset.Parse("2026-08-29T17:00:00Z"),
                DateTimeOffset.Parse("2026-08-29T18:00:00Z"), null, true,
                "application_restarted",
                "Harness.NET restarted before this terminal session completed.");
        }

        public ValueTask<DeveloperTerminalStartResult> StartAsync(
            DeveloperTerminalStartRequest request,
            CancellationToken cancellationToken = default)
        {
            StartCalls++;
            current = new(
                new("terminal-1"),
                request.Workspace.WorkspaceId,
                new(request.Workspace.WorkspaceId, request.Workspace.GoalId, new("main"),
                    WorkbenchWorkspaceScope.OriginalWorkspace,
                    "Original workspace · user-editable source context"),
                new("."),
                new("bash"),
                new("Inherited host environment with locked terminal policy"),
                new("Transient · never included in model context"),
                request.Dimensions,
                DeveloperTerminalSessionState.Running,
                DateTimeOffset.Parse("2026-08-29T18:00:00Z"),
                null,
                null,
                true,
                null,
                null);
            return ValueTask.FromResult(new DeveloperTerminalStartResult(current, null, null));
        }

        public ValueTask<DeveloperTerminalListResult> ListAsync(
            WorkbenchWorkspaceRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                new DeveloperTerminalListResult(current is null ? [] : [current]));

        public ValueTask<DeveloperTerminalSessionResult> GetAsync(
            DeveloperTerminalSessionId sessionId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                new DeveloperTerminalSessionResult(current, null, null));

        public async ValueTask<DeveloperTerminalReadResult> ReadAsync(
            DeveloperTerminalSessionId sessionId,
            CancellationToken cancellationToken = default)
        {
            if (!outputRead)
            {
                outputRead = true;
                return new(new(Encoding.UTF8.GetBytes(
                    "Grüße from PTY\r\nhttps://example.test/docs\r\n")), false, null, null);
            }

            await stopped.Task.WaitAsync(cancellationToken);
            return new(new(ReadOnlyMemory<byte>.Empty), true, null, null);
        }

        public ValueTask<DeveloperTerminalSessionResult> WriteAsync(
            DeveloperTerminalSessionId sessionId,
            DeveloperTerminalData data,
            CancellationToken cancellationToken = default)
        {
            Written = data.Value.ToArray();
            return ValueTask.FromResult(new DeveloperTerminalSessionResult(current, null, null));
        }

        public ValueTask<DeveloperTerminalSessionResult> ResizeAsync(
            DeveloperTerminalSessionId sessionId,
            DeveloperTerminalDimensions dimensions,
            CancellationToken cancellationToken = default)
        {
            current = current! with { Dimensions = dimensions };
            return ValueTask.FromResult(new DeveloperTerminalSessionResult(current, null, null));
        }

        public ValueTask<DeveloperTerminalSessionResult> StopAsync(
            DeveloperTerminalSessionId sessionId,
            CancellationToken cancellationToken = default)
        {
            current = current! with
            {
                State = DeveloperTerminalSessionState.Stopped,
                CompletedAt = DateTimeOffset.Parse("2026-08-29T18:01:00Z"),
                ExitCode = 129,
            };
            stopped.TrySetResult();
            return ValueTask.FromResult(new DeveloperTerminalSessionResult(current, null, null));
        }
    }

    private sealed class SensitiveGuard : ISensitiveDisplayGuard
    {
        public bool IsSensitive { get; private set; }
        public SensitiveDisplayStatus Current => new(IsSensitive,
            IsSensitive ? SensitiveDisplayKind.DeveloperTerminal : null, 0);

        public bool TryBeginSensitiveDisplay(
            SensitiveDisplayKind kind,
            out ISensitiveDisplayLease? lease)
        {
            if (IsSensitive)
            {
                lease = null;
                return false;
            }

            IsSensitive = true;
            lease = new Lease(this);
            return true;
        }

        public bool TryBeginVisualCapture(out ISensitiveDisplayLease? lease)
        {
            lease = null;
            return !IsSensitive;
        }

        private sealed class Lease(SensitiveGuard guard) : ISensitiveDisplayLease
        {
            public void Dispose() => guard.IsSensitive = false;
        }
    }
}
