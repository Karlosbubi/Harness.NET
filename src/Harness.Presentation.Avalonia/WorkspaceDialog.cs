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
    private readonly IWorkspaceFolderPicker folderPicker;
    private readonly bool browseOnOpen;
    private readonly IDisposable subscription;
    private readonly ListBox registered = new();
    private readonly TextBox repositoryPath = new();
    private readonly ListBox entryPoints = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button useSelected = new() { Content = "Use selected" };
    private readonly Button trust = new() { Content = "Trust…" };
    private readonly Button browse = new() { Content = "Browse…" };
    private readonly Button inspect = new() { Content = "Scan" };
    private readonly Button register = new() { Content = "Add workspace" };
    private bool rendering;

    internal WorkspaceDialog(
        AvaloniaPresentationStore store,
        CancellationToken cancellationToken,
        IWorkspaceFolderPicker? folderPicker = null,
        bool browseOnOpen = false)
    {
        this.store = store;
        this.cancellationToken = cancellationToken;
        this.folderPicker = folderPicker ?? new AvaloniaWorkspaceFolderPicker();
        this.browseOnOpen = browseOnOpen;
        Title = "Manage workspaces";
        Width = 900;
        Height = 640;
        MinWidth = 720;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
        WireInteractions();
        subscription = store.States.Subscribe(state =>
            Dispatcher.UIThread.Post(() => Render(state.Workspaces)));
        Closed += (_, _) => subscription.Dispose();
        Opened += async (_, _) =>
        {
            await store.RefreshWorkspacesAsync(cancellationToken);
            if (this.browseOnOpen)
            {
                await BrowseRepositoryAsync();
            }
        };
    }

    private Control BuildContent()
    {
        Grid root = new()
        {
            RowDefinitions = new("Auto,*,Auto"),
            Margin = new(24),
            RowSpacing = 20,
        };
        StackPanel heading = new()
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = "Workspaces",
                    FontSize = 24,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = "Open a Git-backed .NET repository or switch between repositories you already added.",
                    TextWrapping = TextWrapping.Wrap,
                    Classes = { "muted" },
                },
            },
        };
        root.Children.Add(heading);

        Grid body = new()
        {
            ColumnDefinitions = new("0.85*,1.15*"),
            ColumnSpacing = 18,
        };
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        Grid existing = new()
        {
            RowDefinitions = new("Auto,*,Auto"),
            RowSpacing = 12,
        };
        existing.Children.Add(new TextBlock
        {
            Text = "YOUR WORKSPACES",
            Classes = { "eyebrow" },
        });
        AutomationProperties.SetName(registered, "Registered workspaces");
        Grid.SetRow(registered, 1);
        existing.Children.Add(registered);
        StackPanel registeredActions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { useSelected, trust },
        };
        Grid.SetRow(registeredActions, 2);
        existing.Children.Add(registeredActions);
        body.Children.Add(new Border
        {
            Classes = { "card" },
            Child = existing,
        });

        Grid add = new()
        {
            RowDefinitions = new("Auto,Auto,Auto,Auto,*,Auto,Auto"),
            RowSpacing = 10,
        };
        add.Children.Add(new TextBlock
        {
            Text = "OPEN A REPOSITORY",
            Classes = { "eyebrow" },
        });
        TextBlock explanation = new()
        {
            Text = "Choose the repository folder. Harness.NET will inspect Git-tracked solutions and projects before anything is registered.",
            TextWrapping = TextWrapping.Wrap,
            Classes = { "muted" },
        };
        Grid.SetRow(explanation, 1);
        add.Children.Add(explanation);

        Grid pathRow = new()
        {
            ColumnDefinitions = new("*,Auto,Auto"),
            ColumnSpacing = 8,
        };
        repositoryPath.PlaceholderText = "Repository folder";
        repositoryPath.Classes.Add("workspace-input");
        AutomationProperties.SetName(repositoryPath, "Repository path");
        pathRow.Children.Add(repositoryPath);
        Grid.SetColumn(browse, 1);
        pathRow.Children.Add(browse);
        Grid.SetColumn(inspect, 2);
        pathRow.Children.Add(inspect);
        Grid.SetRow(pathRow, 2);
        add.Children.Add(pathRow);

        TextBlock entryHeading = new()
        {
            Text = "Choose a solution or project",
            FontWeight = FontWeight.SemiBold,
        };
        Grid.SetRow(entryHeading, 3);
        add.Children.Add(entryHeading);

        AutomationProperties.SetName(entryPoints, "Tracked .NET entry points");
        Grid.SetRow(entryPoints, 4);
        add.Children.Add(entryPoints);

        Grid.SetRow(register, 5);
        register.HorizontalAlignment = HorizontalAlignment.Left;
        add.Children.Add(register);

        Grid.SetRow(status, 6);
        AutomationProperties.SetName(status, "Workspace operation status");
        add.Children.Add(status);
        Border addCard = new()
        {
            Classes = { "card" },
            Child = add,
        };
        Grid.SetColumn(addCard, 1);
        body.Children.Add(addCard);

        Button close = new() { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        Grid.SetRow(close, 2);
        root.Children.Add(close);

        foreach (Button button in new[] { useSelected, trust, browse, inspect, close })
        {
            button.Classes.Add("command");
        }
        register.Classes.Add("primary");
        AutomationProperties.SetName(browse, "Browse for repository folder");
        AutomationProperties.SetName(inspect, "Inspect");
        AutomationProperties.SetName(register, "Register");
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
        browse.Click += async (_, _) => await BrowseRepositoryAsync();
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
            browse.IsEnabled = !state.IsBusy;
            register.IsEnabled = !state.IsBusy && entries.Length > 0;
            status.Text = state.IsBusy ? "Working…" : state.Status ?? string.Empty;
        }
        finally
        {
            rendering = false;
        }
    }

    internal async Task BrowseRepositoryAsync()
    {
        string current = store.Current.Workspaces.RepositoryPath.Trim();
        WorkspaceFolderPickerResult result = await folderPicker.PickAsync(
            this,
            current.Length == 0 ? null : new(current),
            cancellationToken);
        if (result.Error is not null)
        {
            store.SetWorkspaceStatus(result.Error);
            return;
        }

        if (result.Folder is null)
        {
            return;
        }

        store.SetRepositoryPath(result.Folder.Value);
        await store.InspectWorkspaceAsync(cancellationToken);
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
        Height = 320;
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
                    Text = "Trust allows repository-local build and test operations. It also " +
                           "allows code intelligence to evaluate project files and run configured " +
                           "analyzers or source generators, which may execute repository or package " +
                           "code. Network, restore, destructive actions, and commits still require " +
                           "separate approval.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock { Text = workspace.RootPath, TextWrapping = TextWrapping.Wrap },
                actions,
            },
        };
    }
}
