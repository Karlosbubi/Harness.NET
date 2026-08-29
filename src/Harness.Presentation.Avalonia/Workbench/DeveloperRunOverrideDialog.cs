using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Harness.BusinessLogic.Execution;
using Harness.UI.Avalonia;

namespace Harness.Presentation.Avalonia.Workbench;

internal sealed record DeveloperRunOverrideDialogResult(
    DeveloperRunOverrides Overrides,
    DeveloperRunMode Mode);

internal enum DeveloperRunOverridePurpose
{
    Run,
    Debug,
}

internal sealed class DeveloperRunOverrideDialog : Window
{
    private readonly TextBox profile = new() { PlaceholderText = "Optional inspected profile" };
    private readonly TextBox workingDirectory = new()
    {
        PlaceholderText = "Optional workspace-relative directory",
    };
    private readonly TextBox arguments = new()
    {
        AcceptsReturn = true,
        MinHeight = 90,
        PlaceholderText = "One application argument per line",
    };
    private readonly TextBox environment = new()
    {
        AcceptsReturn = true,
        MinHeight = 90,
        PlaceholderText = "One NAME=value override per line",
    };
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StatusIndicator status = new();
    private readonly CheckBox hotReload = new() { Content = "Keep running with Hot Reload" };
    private readonly DeveloperRunOverridePurpose purpose;

    internal DeveloperRunOverrideDialog(
        string projectPath,
        DeveloperRunOverridePurpose purpose = DeveloperRunOverridePurpose.Run)
    {
        this.purpose = purpose;
        Title = purpose is DeveloperRunOverridePurpose.Debug
            ? "Debug launch overrides"
            : "One-run overrides";
        Width = 560;
        Height = 620;
        MinWidth = 440;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(this, purpose is DeveloperRunOverridePurpose.Debug
            ? $"Debug launch overrides for {projectPath}"
            : $"One-run overrides for {projectPath}");
        Content = BuildContent(projectPath);
        foreach (TextBox input in new[] { profile, workingDirectory, arguments, environment })
            input.TextChanged += (_, _) => UpdateSummary();
        hotReload.IsCheckedChanged += (_, _) => UpdateSummary();
        UpdateSummary();
    }

    internal TextBox Profile => profile;
    internal TextBox WorkingDirectory => workingDirectory;
    internal TextBox Arguments => arguments;
    internal TextBox Environment => environment;
    internal string Summary => summary.Text ?? string.Empty;
    internal CheckBox HotReload => hotReload;

    private Control BuildContent(string projectPath)
    {
        StackPanel content = new() { Spacing = 8, Margin = new(18) };
        content.Children.Add(new TextBlock
        {
            Text = purpose is DeveloperRunOverridePurpose.Debug
                ? $"Debug {projectPath} with explicit launch overrides"
                : $"Run {projectPath} once with explicit overrides",
            FontWeight = FontWeight.SemiBold,
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new TextBlock
        {
            Text = "Values are passed directly to dotnet without a shell and are not persisted. " +
                   "Profile names are revalidated against the exact inspected project.",
            TextWrapping = TextWrapping.Wrap,
        });
        AddField(content, "Launch profile", profile, "One-run launch profile");
        AddField(content, "Working directory", workingDirectory,
            "One-run working directory");
        AddField(content, "Application arguments", arguments, "One-run arguments");
        AddField(content, "Environment overrides", environment, "One-run environment");
        if (purpose is DeveloperRunOverridePurpose.Run)
        {
            AutomationProperties.SetName(hotReload, "Use Hot Reload for this run");
            content.Children.Add(hotReload);
        }
        AutomationProperties.SetName(summary, "One-run override summary");
        content.Children.Add(summary);
        AutomationProperties.SetName(status, "One-run override validation");
        content.Children.Add(status);
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close(null);
        Button run = new()
        {
            Content = purpose is DeveloperRunOverridePurpose.Debug ? "Start debugging" : "Run once",
        };
        run.Classes.Add("primary");
        AutomationProperties.SetName(run, purpose is DeveloperRunOverridePurpose.Debug
            ? "Start managed debugging with launch overrides"
            : "Run with one-run overrides");
        run.Click += (_, _) => Submit();
        actions.Children.Add(cancel);
        actions.Children.Add(run);
        content.Children.Add(actions);
        return new ScrollViewer { Content = content };
    }

    private static void AddField(
        Panel parent,
        string label,
        TextBox input,
        string accessibleName)
    {
        parent.Children.Add(new TextBlock { Text = label });
        AutomationProperties.SetName(input, accessibleName);
        parent.Children.Add(input);
    }

    private void Submit()
    {
        if (!TryCreate(out DeveloperRunOverrides? overrides, out string? error))
        {
            status.Message = error ?? "The one-run overrides are invalid.";
            status.Severity = StatusSeverity.Error;
            return;
        }
        Close(new DeveloperRunOverrideDialogResult(
            overrides!, purpose is DeveloperRunOverridePurpose.Run && hotReload.IsChecked is true
                ? DeveloperRunMode.HotReload
                : DeveloperRunMode.Standard));
    }

    internal bool TryCreate(out DeveloperRunOverrides? overrides, out string? error)
    {
        UpdateSummary();
        overrides = null;
        error = null;
        string[] argumentValues = Lines(arguments.Text);
        string[] environmentValues = Lines(environment.Text);
        List<DeveloperLaunchEnvironmentVariable> variables = [];
        foreach (string item in environmentValues)
        {
            int separator = item.IndexOf('=');
            if (separator <= 0)
            {
                error = "Each environment override must use NAME=value.";
                return false;
            }
            variables.Add(new(
                new(item[..separator]),
                new(item[(separator + 1)..])));
        }
        overrides = new(
            Optional(profile.Text, value => new DeveloperLaunchProfileName(value)),
            argumentValues.Select(value => new DeveloperLaunchArgument(value)).ToImmutableArray(),
            variables.ToImmutableArray(),
            Optional(workingDirectory.Text,
                value => new DeveloperLaunchWorkingDirectory(value)));
        return true;
    }

    private void UpdateSummary()
    {
        string[] argumentValues = Lines(arguments.Text);
        string[] environmentValues = Lines(environment.Text);
        string names = string.Join(", ", environmentValues.Select(value =>
        {
            int separator = value.IndexOf('=');
            return separator <= 0 ? "invalid entry" : value[..separator];
        }));
        string mode = purpose is DeveloperRunOverridePurpose.Debug
            ? "Debug"
            : hotReload.IsChecked is true ? "Hot Reload" : "Run";
        summary.Text = $"Mode: {mode} · " +
                       $"profile: {(profile.Text?.Trim() is { Length: > 0 } selected ? selected : "none")} · " +
                       $"arguments: {argumentValues.Length} · environment names: " +
                       $"{(names.Length == 0 ? "none" : names)} · working directory: " +
                       $"{(workingDirectory.Text?.Trim() is { Length: > 0 } directory ? directory : "workspace root")}.";
    }

    private static string[] Lines(string? value) => (value ?? string.Empty)
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Trim())
        .Where(line => line.Length > 0)
        .ToArray();

    private static T? Optional<T>(string? value, Func<string, T> create) where T : class =>
        string.IsNullOrWhiteSpace(value) ? null : create(value.Trim());
}
