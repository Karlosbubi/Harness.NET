using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Harness.BusinessLogic.Workspaces;

namespace Harness.Presentation.Avalonia;

internal sealed class WorkspaceDialog : Window
{
    private readonly AvaloniaPresentationStore store;
    private readonly CancellationToken cancellationToken;
    private readonly IDisposable subscription;
    private readonly ListBox registered = new();
    private readonly TextBox repositoryPath = new();
    private readonly ListBox entryPoints = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button useSelected = new() { Content = "Use selected" };
    private readonly Button trust = new() { Content = "Trust…" };
    private readonly Button inspect = new() { Content = "Inspect" };
    private readonly Button register = new() { Content = "Register" };
    private bool rendering;

    internal WorkspaceDialog(
        AvaloniaPresentationStore store,
        CancellationToken cancellationToken)
    {
        this.store = store;
        this.cancellationToken = cancellationToken;
        Title = "Manage workspaces";
        Width = 760;
        Height = 620;
        MinWidth = 620;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
        WireInteractions();
        subscription = store.States.Subscribe(state =>
            Dispatcher.UIThread.Post(() => Render(state.Workspaces)));
        Closed += (_, _) => subscription.Dispose();
        Opened += async (_, _) => await store.RefreshWorkspacesAsync(cancellationToken);
    }

    private Control BuildContent()
    {
        Grid root = new()
        {
            RowDefinitions = new("Auto,180,Auto,Auto,150,Auto,*,Auto"),
            Margin = new(20),
            RowSpacing = 10,
        };
        root.Children.Add(new TextBlock
        {
            Text = "Registered workspaces",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
        });

        AutomationProperties.SetName(registered, "Registered workspaces");
        Grid.SetRow(registered, 1);
        root.Children.Add(registered);

        StackPanel registeredActions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { useSelected, trust },
        };
        Grid.SetRow(registeredActions, 2);
        root.Children.Add(registeredActions);

        Grid pathRow = new()
        {
            ColumnDefinitions = new("*,Auto"),
            ColumnSpacing = 8,
        };
        repositoryPath.PlaceholderText = "/path/to/git/repository";
        AutomationProperties.SetName(repositoryPath, "Repository path");
        pathRow.Children.Add(repositoryPath);
        Grid.SetColumn(inspect, 1);
        pathRow.Children.Add(inspect);
        Grid.SetRow(pathRow, 3);
        root.Children.Add(pathRow);

        AutomationProperties.SetName(entryPoints, "Tracked .NET entry points");
        Grid.SetRow(entryPoints, 4);
        root.Children.Add(entryPoints);

        Grid.SetRow(register, 5);
        register.HorizontalAlignment = HorizontalAlignment.Left;
        root.Children.Add(register);

        Grid.SetRow(status, 6);
        root.Children.Add(status);

        Button close = new() { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        Grid.SetRow(close, 7);
        root.Children.Add(close);
        return root;
    }

    private void WireInteractions()
    {
        repositoryPath.GetObservable(TextBox.TextProperty).Subscribe(value =>
        {
            if (!rendering && value != store.Current.Workspaces.RepositoryPath)
            {
                store.SetRepositoryPath(value ?? string.Empty);
            }
        });
        repositoryPath.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                args.Handled = true;
                await store.InspectWorkspaceAsync(cancellationToken);
            }
        };
        inspect.Click += async (_, _) => await store.InspectWorkspaceAsync(cancellationToken);
        registered.SelectionChanged += (_, _) =>
        {
            if (registered.SelectedItem is WorkspaceChoice choice)
            {
                trust.Content = choice.Workspace.IsTrusted ? "Revoke trust" : "Trust…";
            }
        };
        register.Click += async (_, _) =>
        {
            if (entryPoints.SelectedItem is EntryPointChoice choice)
            {
                await store.RegisterWorkspaceAsync(choice.Path, cancellationToken);
            }
        };
        useSelected.Click += async (_, _) =>
        {
            if (registered.SelectedItem is WorkspaceChoice choice)
            {
                await store.SelectWorkspaceAsync(choice.Workspace.Id, cancellationToken);
            }
        };
        trust.Click += async (_, _) =>
        {
            if (registered.SelectedItem is not WorkspaceChoice choice)
            {
                return;
            }

            bool newTrust = !choice.Workspace.IsTrusted;
            if (newTrust && !await ConfirmTrustAsync(choice.Workspace))
            {
                return;
            }

            await store.SetWorkspaceTrustAsync(
                choice.Workspace.Id,
                newTrust,
                cancellationToken);
        };
    }

    private void Render(WorkspaceManagementState state)
    {
        rendering = true;
        try
        {
            repositoryPath.Text = state.RepositoryPath;
            WorkspaceChoice[] workspaces = state.Registered
                .Select(workspace => new WorkspaceChoice(workspace))
                .ToArray();
            string? selectedId = (registered.SelectedItem as WorkspaceChoice)?.Workspace.Id;
            registered.ItemsSource = workspaces;
            registered.SelectedItem = workspaces.FirstOrDefault(item =>
                item.Workspace.Id == selectedId) ?? workspaces.FirstOrDefault(item =>
                item.Workspace.IsActive);

            EntryPointChoice[] entries = state.EntryPoints
                .Select(path => new EntryPointChoice(path, DisplayEntryPoint(state.RepositoryPath, path)))
                .ToArray();
            entryPoints.ItemsSource = entries;
            if (entries.Length > 0 && entryPoints.SelectedItem is null)
            {
                entryPoints.SelectedIndex = 0;
            }

            bool hasWorkspace = registered.SelectedItem is WorkspaceChoice;
            useSelected.IsEnabled = !state.IsBusy && hasWorkspace;
            trust.IsEnabled = !state.IsBusy && hasWorkspace;
            trust.Content = (registered.SelectedItem as WorkspaceChoice)?.Workspace.IsTrusted is true
                ? "Revoke trust"
                : "Trust…";
            inspect.IsEnabled = !state.IsBusy;
            register.IsEnabled = !state.IsBusy && entries.Length > 0;
            status.Text = state.IsBusy ? "Working…" : state.Status ?? string.Empty;
        }
        finally
        {
            rendering = false;
        }
    }

    private async Task<bool> ConfirmTrustAsync(WorkspaceView workspace)
    {
        TrustConfirmationDialog dialog = new(workspace);
        return await dialog.ShowDialog<bool>(this);
    }

    private static string DisplayEntryPoint(string repositoryPath, string entryPoint)
    {
        try
        {
            return string.IsNullOrWhiteSpace(repositoryPath)
                ? entryPoint
                : Path.GetRelativePath(repositoryPath, entryPoint);
        }
        catch (ArgumentException)
        {
            return entryPoint;
        }
    }

    private sealed record WorkspaceChoice(WorkspaceView Workspace)
    {
        public override string ToString() =>
            $"{(Workspace.IsActive ? "●" : "○")} {Workspace.Name} — " +
            $"{(Workspace.IsTrusted ? "trusted" : "untrusted")} — {Workspace.Branch}";
    }

    private sealed record EntryPointChoice(string Path, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}

internal sealed class TrustConfirmationDialog : Window
{
    internal TrustConfirmationDialog(WorkspaceView workspace)
    {
        Title = "Trust workspace";
        Width = 520;
        Height = 250;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Button cancel = new() { Content = "Cancel" };
        cancel.Click += (_, _) => Close(false);
        Button confirm = new() { Content = "Trust workspace" };
        confirm.Click += (_, _) => Close(true);
        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, confirm },
        };
        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = $"Trust {workspace.Name}?",
                    FontSize = 17,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = "Trust allows approved goals to run repository-local build and test " +
                           "operations. Network, restore, destructive actions, and commits still " +
                           "require separate approval.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock { Text = workspace.RootPath, TextWrapping = TextWrapping.Wrap },
                actions,
            },
        };
    }
}
