using System.Collections.ObjectModel;
using Harness.BusinessLogic.Workspaces;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Harness.Presentation.Terminal;

internal sealed class WorkspaceDialog : Dialog<WorkspaceView>
{
    private readonly IApplication application;
    private readonly IWorkspaceService workspaceService;
    private readonly CancellationToken cancellationToken;
    private readonly IReadOnlyList<WorkspaceView> registeredWorkspaces;
    private readonly ListView registeredList;
    private readonly TextField repositoryPath;
    private readonly ListView entryPointList;
    private readonly Button inspect;
    private readonly Button register;
    private readonly Button useSelected;
    private readonly Label status;
    private string[] entryPoints = [];

    internal WorkspaceDialog(
        IApplication application,
        IWorkspaceService workspaceService,
        IReadOnlyList<WorkspaceView> registeredWorkspaces,
        string initialPath,
        CancellationToken cancellationToken)
    {
        this.application = application;
        this.workspaceService = workspaceService;
        this.registeredWorkspaces = registeredWorkspaces;
        this.cancellationToken = cancellationToken;

        Title = "Workspaces";
        Width = Dim.Percent(80);
        Height = 22;

        Add(new Label { Text = "Registered", X = 0, Y = 0 });
        registeredList = new()
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = 4,
        };
        registeredList.SetSource(new ObservableCollection<string>(registeredWorkspaces
            .Select(workspace =>
                $"{(workspace.IsActive ? "*" : " ")} {workspace.Name} | " +
                $"{(workspace.IsTrusted ? "trusted" : "untrusted")} | {workspace.Branch}")
            .ToArray()));
        int activeIndex = registeredWorkspaces
            .Select((workspace, index) => (workspace, index))
            .FirstOrDefault(item => item.workspace.IsActive)
            .index;
        if (registeredWorkspaces.Count > 0)
        {
            registeredList.SelectedItem = activeIndex;
        }

        useSelected = new()
        {
            Title = "_Use selected",
            X = 0,
            Y = 5,
            Width = 16,
            Enabled = registeredWorkspaces.Count > 0,
        };
        useSelected.Accepting += async (_, args) =>
        {
            args.Handled = true;
            await SelectRegisteredAsync();
        };
        registeredList.Accepting += async (_, args) =>
        {
            args.Handled = true;
            await SelectRegisteredAsync();
        };

        Add(new Label { Text = "Repository path", X = 0, Y = 7 });
        repositoryPath = new()
        {
            Text = initialPath,
            X = 0,
            Y = 8,
            Width = Dim.Fill(14),
        };
        inspect = new()
        {
            Title = "_Inspect",
            X = Pos.AnchorEnd(12),
            Y = 8,
            Width = 12,
        };
        inspect.Accepting += async (_, args) =>
        {
            args.Handled = true;
            await InspectAsync();
        };
        repositoryPath.Accepting += async (_, args) =>
        {
            args.Handled = true;
            await InspectAsync();
        };

        Add(new Label { Text = "Tracked .NET entry points", X = 0, Y = 10 });
        entryPointList = new()
        {
            X = 0,
            Y = 11,
            Width = Dim.Fill(),
            Height = 4,
        };
        register = new()
        {
            Title = "_Register",
            X = 0,
            Y = 15,
            Width = 13,
            Enabled = false,
        };
        register.Accepting += async (_, args) =>
        {
            args.Handled = true;
            await RegisterAsync();
        };
        entryPointList.Accepting += async (_, args) =>
        {
            args.Handled = true;
            await RegisterAsync();
        };
        status = new()
        {
            X = Pos.Right(register) + 1,
            Y = 15,
            Width = Dim.Fill(),
            Height = 2,
        };

        Add(
            registeredList,
            useSelected,
            repositoryPath,
            inspect,
            entryPointList,
            register,
            status);
        AddButton(new Button { Title = "_Close" });
    }

    private async Task InspectAsync()
    {
        string path = repositoryPath.Text?.ToString() ?? string.Empty;
        await RunCommandAsync(async () =>
        {
            WorkspaceResult result = await workspaceService.InspectAsync(path, cancellationToken);
            application.Invoke(() =>
            {
                entryPoints = result.EntryPoints.ToArray();
                entryPointList.SetSource(new ObservableCollection<string>(entryPoints
                    .Select(entryPoint => Path.GetRelativePath(
                        result.Workspace?.RootPath ?? path,
                        entryPoint))
                    .ToArray()));
                register.Enabled = entryPoints.Length > 0;
                status.Text = result.Error ?? $"Found {entryPoints.Length} entry point(s)";
            });
        });
    }

    private async Task RegisterAsync()
    {
        int selected = entryPointList.SelectedItem ?? -1;
        if (selected < 0 || selected >= entryPoints.Length)
        {
            status.Text = "Select an entry point";
            return;
        }

        string path = repositoryPath.Text?.ToString() ?? string.Empty;
        await RunCommandAsync(async () =>
        {
            WorkspaceResult result = await workspaceService.RegisterAsync(
                path,
                entryPoints[selected],
                cancellationToken);
            if (result.Workspace is null)
            {
                application.Invoke(() => status.Text = result.Error ?? "Registration failed");
                return;
            }

            application.Invoke(() =>
            {
                Result = result.Workspace;
                RequestStop();
            });
        });
    }

    private async Task SelectRegisteredAsync()
    {
        int selected = registeredList.SelectedItem ?? -1;
        if (selected < 0 || selected >= registeredWorkspaces.Count)
        {
            status.Text = "Select a registered workspace";
            return;
        }

        await RunCommandAsync(async () =>
        {
            WorkspaceView selectedWorkspace = await workspaceService.SelectAsync(
                registeredWorkspaces[selected].Id,
                cancellationToken);
            application.Invoke(() =>
            {
                Result = selectedWorkspace;
                RequestStop();
            });
        });
    }

    private async Task RunCommandAsync(Func<Task> command)
    {
        try
        {
            SetCommandsEnabled(false);
            await command();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RequestStop();
        }
        catch (Exception exception)
        {
            application.Invoke(() => status.Text = exception.Message);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                application.Invoke(() => SetCommandsEnabled(true));
            }
        }
    }

    private void SetCommandsEnabled(bool enabled)
    {
        inspect.Enabled = enabled;
        register.Enabled = enabled && entryPoints.Length > 0;
        useSelected.Enabled = enabled && registeredWorkspaces.Count > 0;
    }
}
