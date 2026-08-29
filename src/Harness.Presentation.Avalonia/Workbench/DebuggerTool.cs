using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using Harness.BusinessLogic.Debugging;
using Harness.BusinessLogic.Goals;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed class DebuggerTool
{
    private readonly IDeveloperDebuggerService? debugger;
    private readonly Func<string, int, GoalId?, ValueTask> navigate;
    private readonly CancellationToken cancellationToken;
    private readonly StatusIndicator status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListBox threads = new();
    private readonly ListBox stack = new();
    private readonly ListBox scopes = new();
    private readonly ListBox variables = new();
    private readonly TextEditor output = CodeEditorView.Create(
        string.Empty, isReadOnly: true, wordWrap: false, showLineNumbers: false,
        path: "debug-output.txt");
    private readonly Button resume = new() { Content = "Continue", IsEnabled = false };
    private readonly Button pause = new() { Content = "Pause", IsEnabled = false };
    private readonly Button stepOver = new() { Content = "Step over", IsEnabled = false };
    private readonly Button stepIn = new() { Content = "Step in", IsEnabled = false };
    private readonly Button stepOut = new() { Content = "Step out", IsEnabled = false };
    private readonly Button stop = new() { Content = "Stop", IsEnabled = false };
    private readonly Button openFrame = new() { Content = "Open frame", IsEnabled = false };
    private DeveloperDebugSessionView? current;
    private DeveloperDebugThreadId? commandThread;
    private int trackingVersion;
    private bool busy;

    internal DebuggerTool(
        IDeveloperDebuggerService? debugger,
        Func<string, int, GoalId?, ValueTask> navigate,
        CancellationToken cancellationToken)
    {
        this.debugger = debugger;
        this.navigate = navigate;
        this.cancellationToken = cancellationToken;
        Content = BuildContent();
        RenderUnavailable(debugger is null
            ? "No managed debugger service is available."
            : "Start Debug from a Roslyn project-entry CodeLens action.");
    }

    internal Control Content { get; }
    internal DeveloperDebugSessionView? Current => current;
    internal string StatusText => status.Message ?? string.Empty;
    internal ListBox Threads => threads;
    internal ListBox Stack => stack;
    internal ListBox Scopes => scopes;
    internal ListBox Variables => variables;

    internal ValueTask TrackAsync(DeveloperDebugSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        current = session;
        commandThread = session.StoppedThreadId ?? session.Threads.FirstOrDefault()?.Id;
        int version = ++trackingVersion;
        Render(session, resetInspection: true);
        _ = PollAsync(session.Id, version);
        return ValueTask.CompletedTask;
    }

    internal async ValueTask RefreshAsync()
    {
        if (debugger is null || current is null || busy) return;
        busy = true;
        try
        {
            DeveloperDebugSessionResult result = await debugger.GetAsync(
                current.Id, cancellationToken);
            if (result.Session is null)
            {
                status.Message = result.Error ?? "The debug session is unavailable.";
                status.Severity = StatusSeverity.Warning;
                return;
            }
            DeveloperDebugSessionView? previous = current;
            current = result.Session;
            commandThread = current.StoppedThreadId ?? commandThread ??
                current.Threads.FirstOrDefault()?.Id;
            bool resetInspection = previous is null || previous.State != current.State ||
                previous.StoppedThreadId != current.StoppedThreadId ||
                !previous.Stack.SequenceEqual(current.Stack);
            Render(current, resetInspection);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            busy = false;
        }
    }

    private Control BuildContent()
    {
        Grid root = new()
        {
            RowDefinitions = new("Auto,Auto,2*,2*,2*"),
            Margin = new(8),
            RowSpacing = 6,
        };
        root.Children.Add(status);
        AutomationProperties.SetName(status, "Managed debugger status");

        WrapPanel commands = new() { Orientation = Orientation.Horizontal };
        AddCommand(commands, resume, "Continue managed debug session",
            () => CommandAsync(DeveloperDebugCommand.Continue));
        AddCommand(commands, pause, "Pause managed debug session",
            () => CommandAsync(DeveloperDebugCommand.Pause));
        AddCommand(commands, stepOver, "Step over in managed debug session",
            () => CommandAsync(DeveloperDebugCommand.StepOver));
        AddCommand(commands, stepIn, "Step into in managed debug session",
            () => CommandAsync(DeveloperDebugCommand.StepIn));
        AddCommand(commands, stepOut, "Step out of managed debug session",
            () => CommandAsync(DeveloperDebugCommand.StepOut));
        AddCommand(commands, stop, "Stop managed debug session", StopAsync);
        AddCommand(commands, openFrame, "Open selected managed stack frame", OpenFrameAsync);
        Grid.SetRow(commands, 1);
        root.Children.Add(commands);

        Grid execution = new() { ColumnDefinitions = new("*,2*"), ColumnSpacing = 8 };
        execution.Children.Add(Section("Threads", threads, "Managed debug threads"));
        Control stackSection = Section("Call stack", stack, "Managed debug call stack");
        Grid.SetColumn(stackSection, 1);
        execution.Children.Add(stackSection);
        Grid.SetRow(execution, 2);
        root.Children.Add(execution);

        Grid inspection = new() { ColumnDefinitions = new("*,2*"), ColumnSpacing = 8 };
        inspection.Children.Add(Section("Scopes", scopes, "Managed debug scopes"));
        Control variableSection = Section("Variables", variables, "Managed debug variables");
        Grid.SetColumn(variableSection, 1);
        inspection.Children.Add(variableSection);
        Grid.SetRow(inspection, 3);
        root.Children.Add(inspection);

        AutomationProperties.SetName(output, "Managed debuggee and adapter output");
        Grid.SetRow(output, 4);
        root.Children.Add(output);

        threads.SelectionChanged += (_, _) =>
        {
            if (threads.SelectedItem is ThreadChoice choice) commandThread = choice.Thread.Id;
        };
        stack.SelectionChanged += async (_, _) => await LoadScopesAsync();
        scopes.SelectionChanged += async (_, _) => await LoadVariablesAsync();
        variables.DoubleTapped += async (_, _) => await ExpandVariableAsync();
        stack.DoubleTapped += async (_, _) => await OpenFrameAsync();
        return root;
    }

    private static Control Section(string heading, ListBox list, string automationName)
    {
        Grid grid = new() { RowDefinitions = new("Auto,*"), RowSpacing = 4 };
        grid.Children.Add(new TextBlock
        {
            Text = heading,
            FontWeight = FontWeight.SemiBold,
        });
        AutomationProperties.SetName(list, automationName);
        Grid.SetRow(list, 1);
        grid.Children.Add(list);
        return grid;
    }

    private static void AddCommand(
        Panel panel,
        Button button,
        string automationName,
        Func<ValueTask> action)
    {
        button.Margin = new(0, 0, 6, 0);
        AutomationProperties.SetName(button, automationName);
        button.Click += async (_, _) => await action();
        panel.Children.Add(button);
    }

    private async ValueTask CommandAsync(DeveloperDebugCommand command)
    {
        if (debugger is null || current is null || commandThread is null || busy) return;
        busy = true;
        try
        {
            DeveloperDebugSessionResult result = await debugger.CommandAsync(
                current.Id, command, commandThread, cancellationToken);
            Apply(result);
        }
        finally
        {
            busy = false;
        }
    }

    private async ValueTask StopAsync()
    {
        if (debugger is null || current is null || busy) return;
        busy = true;
        try
        {
            DeveloperDebugSessionResult result = await debugger.StopAsync(
                current.Id, cancellationToken);
            Apply(result);
        }
        finally
        {
            busy = false;
        }
    }

    private async ValueTask LoadScopesAsync()
    {
        if (debugger is null || current is null ||
            stack.SelectedItem is not StackChoice choice) return;
        DeveloperDebugInspectionResult<DeveloperDebugScope> result =
            await debugger.GetScopesAsync(current.Id, choice.Frame.Id, cancellationToken);
        scopes.ItemsSource = result.Items.Select(item => new ScopeChoice(item)).ToArray();
        variables.ItemsSource = Array.Empty<VariableChoice>();
        if (result.Error is not null)
        {
            status.Message = result.Error;
            status.Severity = StatusSeverity.Warning;
        }
        else if (result.Items.Count > 0)
        {
            scopes.SelectedIndex = 0;
        }
    }

    private async ValueTask LoadVariablesAsync()
    {
        if (scopes.SelectedItem is ScopeChoice choice)
            await LoadVariablesAsync(choice.Scope.VariablesReference);
    }

    private async ValueTask ExpandVariableAsync()
    {
        if (variables.SelectedItem is VariableChoice
            {
                Variable.VariablesReference.Value: > 0,
            } choice)
        {
            await LoadVariablesAsync(choice.Variable.VariablesReference);
        }
    }

    private async ValueTask LoadVariablesAsync(DeveloperDebugVariablesReference reference)
    {
        if (debugger is null || current is null) return;
        DeveloperDebugInspectionResult<DeveloperDebugVariable> result =
            await debugger.GetVariablesAsync(current.Id, reference, cancellationToken);
        variables.ItemsSource = result.Items.Select(item => new VariableChoice(item)).ToArray();
        if (result.Error is not null)
        {
            status.Message = result.Error;
            status.Severity = StatusSeverity.Warning;
        }
    }

    private async ValueTask OpenFrameAsync()
    {
        if (current is null || stack.SelectedItem is not StackChoice
            {
                Frame.Source: { } source,
                Frame.Line: { } line,
            }) return;
        await navigate(source.Value, line.Value, current.GoalId);
    }

    private void Apply(DeveloperDebugSessionResult result)
    {
        if (result.Session is not null)
        {
            current = result.Session;
            Render(current, resetInspection: true);
        }
        if (result.Error is not null)
        {
            status.Message = result.Error;
            status.Severity = StatusSeverity.Warning;
        }
    }

    private void Render(DeveloperDebugSessionView session, bool resetInspection)
    {
        current = session;
        bool terminal = session.State is DeveloperDebugSessionState.Succeeded or
            DeveloperDebugSessionState.Failed or DeveloperDebugSessionState.Terminated or
            DeveloperDebugSessionState.Interrupted;
        bool stopped = session.State is DeveloperDebugSessionState.Stopped;
        status.Message = $"{session.Status} · {session.Project.ProjectPath.Value} · " +
                         $"{session.SourceDescription}";
        status.Severity = session.State is DeveloperDebugSessionState.Failed
            ? StatusSeverity.Error
            : terminal ? StatusSeverity.Success : StatusSeverity.Information;
        resume.IsEnabled = stopped;
        pause.IsEnabled = session.State is DeveloperDebugSessionState.Running &&
                          commandThread is not null;
        stepOver.IsEnabled = stopped;
        stepIn.IsEnabled = stopped;
        stepOut.IsEnabled = stopped;
        stop.IsEnabled = !terminal;
        output.Text = session.Output.Value;
        openFrame.IsEnabled = session.Stack.Any(item => item.Source is not null && item.Line is not null);
        if (!resetInspection) return;
        threads.ItemsSource = session.Threads.Select(item => new ThreadChoice(item)).ToArray();
        stack.ItemsSource = session.Stack.Select(item => new StackChoice(item)).ToArray();
        scopes.ItemsSource = Array.Empty<ScopeChoice>();
        variables.ItemsSource = Array.Empty<VariableChoice>();
        if (session.Threads.Length > 0) threads.SelectedIndex = 0;
        if (session.Stack.Length > 0) stack.SelectedIndex = 0;
    }

    private void RenderUnavailable(string message)
    {
        status.Message = message;
        status.Severity = StatusSeverity.Information;
        threads.ItemsSource = Array.Empty<ThreadChoice>();
        stack.ItemsSource = Array.Empty<StackChoice>();
        scopes.ItemsSource = Array.Empty<ScopeChoice>();
        variables.ItemsSource = Array.Empty<VariableChoice>();
        output.Text = string.Empty;
    }

    private async Task PollAsync(DeveloperDebugSessionId id, int version)
    {
        try
        {
            for (int attempt = 0; attempt < 14_400 && version == trackingVersion; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                if (version != trackingVersion || current?.Id != id) return;
                await RefreshAsync();
                if (current?.State is DeveloperDebugSessionState.Succeeded or
                    DeveloperDebugSessionState.Failed or DeveloperDebugSessionState.Terminated or
                    DeveloperDebugSessionState.Interrupted) return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private sealed record ThreadChoice(DeveloperDebugThread Thread)
    {
        public override string ToString() => $"{Thread.Id.Value} · {Thread.Name}";
    }

    private sealed record StackChoice(DeveloperDebugStackFrame Frame)
    {
        public override string ToString() => Frame.Source is null || Frame.Line is null
            ? Frame.Name
            : $"{Frame.Name} · {Frame.Source.Value}:{Frame.Line.Value}";
    }

    private sealed record ScopeChoice(DeveloperDebugScope Scope)
    {
        public override string ToString() => Scope.Name + (Scope.IsExpensive ? " · deferred" : "");
    }

    private sealed record VariableChoice(DeveloperDebugVariable Variable)
    {
        public override string ToString() =>
            $"{Variable.Name.Value} = {Variable.Value.Value}" +
            (Variable.Type is null ? string.Empty : $" · {Variable.Type.Value}") +
            (Variable.VariablesReference.Value > 0 ? " ▸" : string.Empty);
    }
}
