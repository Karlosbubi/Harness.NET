using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Harness.BusinessLogic.ProjectSecrets;
using Harness.BusinessLogic.Workspaces;

namespace Harness.Presentation.Avalonia;

internal sealed class ProjectUserSecretsDialog : Window
{
    private readonly IProjectUserSecretsService service;
    private readonly WorkspaceId workspaceId;
    private readonly CancellationToken cancellationToken;
    private readonly ComboBox projects = new();
    private readonly ListBox keys = new() { MinHeight = 100 };
    private readonly TextBox value = new()
    {
        IsReadOnly = true,
        Text = "Select a secret key.",
    };
    private readonly TextBlock projectStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button reveal = new() { Content = "Reveal", IsEnabled = false };
    private readonly Button copy = new() { Content = "Copy", IsEnabled = false };
    private readonly Button add = new() { Content = "Add", IsEnabled = false };
    private readonly Button change = new() { Content = "Change", IsEnabled = false };
    private readonly Button delete = new() { Content = "Delete", IsEnabled = false };
    private ProjectUserSecretDisclosure? disclosure;
    private bool loading;

    internal ProjectUserSecretsDialog(
        IProjectUserSecretsService service,
        WorkspaceId workspaceId,
        CancellationToken cancellationToken)
    {
        this.service = service;
        this.workspaceId = workspaceId;
        this.cancellationToken = cancellationToken;
        Title = "Project User Secrets";
        Width = 760;
        Height = 650;
        MinWidth = 580;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
        WireInteractions();
        Opened += async (_, _) =>
        {
            await LoadProjectsAsync();
            Dispatcher.UIThread.Post(() =>
            {
                projects.IsDropDownOpen = false;
                _ = add.Focus();
            }, DispatcherPriority.Background);
        };
        Closed += (_, _) => HideValue();
    }

    internal ComboBox ProjectSelector => projects;
    internal ListBox SecretKeys => keys;
    internal TextBox SecretValue => value;
    internal Button RevealButton => reveal;
    internal Button CopyButton => copy;
    internal Button AddButton => add;
    internal Button ChangeButton => change;
    internal Button DeleteButton => delete;

    private Control BuildContent()
    {
        AutomationProperties.SetName(projects, "Project User Secrets project");
        AutomationProperties.SetName(keys, "Project User Secret keys");
        AutomationProperties.SetName(value, "Selected project secret value");
        AutomationProperties.SetName(projectStatus, "Project User Secrets project status");
        AutomationProperties.SetName(status, "Project User Secrets operation status");
        AutomationProperties.SetName(reveal, "Reveal selected project secret");
        AutomationProperties.SetName(copy, "Copy selected project secret");
        AutomationProperties.SetName(add, "Add project secret");
        AutomationProperties.SetName(change, "Change selected project secret");
        AutomationProperties.SetName(delete, "Delete selected project secret");

        Button refresh = new() { Content = "Refresh" };
        AutomationProperties.SetName(refresh, "Refresh Project User Secrets");
        refresh.Click += async (_, _) => await RefreshSelectedAsync();
        Button close = new() { Content = "Close" };
        close.Click += (_, _) => Close();

        StackPanel heading = new()
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Text = "Project User Secrets",
                    FontSize = 24,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = "Development-only values from the standard .NET User Secrets store. Values are masked and never sent to models, logs, evidence, search, indexes, or backups.",
                    TextWrapping = TextWrapping.Wrap,
                    Classes = { "muted" },
                },
            },
        };

        Grid selector = new()
        {
            RowDefinitions = new("Auto,Auto,Auto"),
            RowSpacing = 6,
            Children =
            {
                new TextBlock { Text = "Project", FontWeight = FontWeight.SemiBold },
            },
        };
        Grid.SetRow(projects, 1);
        selector.Children.Add(projects);
        Grid.SetRow(projectStatus, 2);
        selector.Children.Add(projectStatus);

        Grid body = new()
        {
            ColumnDefinitions = new("0.85*,1.15*"),
            ColumnSpacing = 16,
        };
        Grid keyPanel = new()
        {
            RowDefinitions = new("Auto,*,Auto"),
            RowSpacing = 8,
            Children = { new TextBlock { Text = "Keys", FontWeight = FontWeight.SemiBold } },
        };
        Grid.SetRow(keys, 1);
        keyPanel.Children.Add(keys);
        StackPanel keyActions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { add, change, delete },
        };
        Grid.SetRow(keyActions, 2);
        keyPanel.Children.Add(keyActions);
        body.Children.Add(new Border { Classes = { "card" }, Child = keyPanel });

        Grid valuePanel = new()
        {
            RowDefinitions = new("Auto,Auto,Auto,*"),
            RowSpacing = 8,
            Children =
            {
                new TextBlock { Text = "Selected value", FontWeight = FontWeight.SemiBold },
            },
        };
        Grid.SetRow(value, 1);
        valuePanel.Children.Add(value);
        StackPanel valueActions = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { reveal, copy },
        };
        Grid.SetRow(valueActions, 2);
        valuePanel.Children.Add(valueActions);
        TextBlock warning = new()
        {
            Text = "Revealing blocks Harness.NET visual capture until you hide the value or close this window. User Secrets are not encrypted and are for development only.",
            TextWrapping = TextWrapping.Wrap,
            Classes = { "muted" },
        };
        Grid.SetRow(warning, 3);
        valuePanel.Children.Add(warning);
        Border valueCard = new() { Classes = { "card" }, Child = valuePanel };
        Grid.SetColumn(valueCard, 1);
        body.Children.Add(valueCard);

        StackPanel footer = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { refresh, close },
        };

        Grid root = new()
        {
            RowDefinitions = new("Auto,Auto,*,Auto,Auto"),
            RowSpacing = 14,
            Margin = new Thickness(22),
            Children = { heading },
        };
        Grid.SetRow(selector, 1);
        root.Children.Add(selector);
        Grid.SetRow(body, 2);
        root.Children.Add(body);
        Grid.SetRow(status, 3);
        root.Children.Add(status);
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);
        return root;
    }

    private void WireInteractions()
    {
        projects.SelectionChanged += async (_, _) =>
        {
            if (!loading)
            {
                await RefreshSelectedAsync();
            }
        };
        keys.SelectionChanged += (_, _) =>
        {
            HideValue();
            UpdateActions();
        };
        reveal.Click += async (_, _) => await ToggleRevealAsync();
        copy.Click += async (_, _) => await CopyAsync();
        add.Click += async (_, _) => await AddAsync();
        change.Click += async (_, _) => await ChangeAsync();
        delete.Click += async (_, _) => await DeleteAsync();
    }

    private async Task LoadProjectsAsync()
    {
        SetBusy(true, "Inspecting projects…");
        ProjectUserSecretsProjectListResult result = await service.ListProjectsAsync(
            workspaceId, cancellationToken);
        ProjectChoice[] choices = result.Projects.Select(project => new ProjectChoice(project)).ToArray();
        loading = true;
        projects.ItemsSource = choices;
        projects.SelectedItem = choices.FirstOrDefault(choice =>
                                    choice.Project.State is ProjectUserSecretsProjectState.Available) ??
                                choices.FirstOrDefault();
        loading = false;
        if (choices.Length == 0)
        {
            projectStatus.Text = result.Error ?? "No .NET projects were found in the active workspace.";
            SetBusy(false, projectStatus.Text);
            return;
        }
        await RefreshSelectedAsync();
        projects.IsDropDownOpen = false;
    }

    private async Task RefreshSelectedAsync()
    {
        HideValue();
        if (projects.SelectedItem is not ProjectChoice choice)
        {
            keys.ItemsSource = null;
            projectStatus.Text = "Select a project.";
            UpdateActions();
            return;
        }

        projectStatus.Text = choice.Project.Status;
        if (choice.Project.State is not ProjectUserSecretsProjectState.Available)
        {
            keys.ItemsSource = null;
            SetBusy(false, choice.Project.Status);
            UpdateActions();
            return;
        }

        SetBusy(true, "Reading secret keys…");
        ProjectUserSecretListResult result = await service.ListAsync(
            workspaceId, choice.Project.Path, cancellationToken);
        SecretKeyChoice[] choices = result.Keys.Select(key => new SecretKeyChoice(key)).ToArray();
        keys.ItemsSource = choices;
        if (choices.Length > 0)
        {
            keys.SelectedIndex = 0;
        }
        projectStatus.Text = result.Project?.Status ?? result.Error ?? "Project User Secrets unavailable.";
        SetBusy(false, result.Error ??
            (choices.Length == 0 ? "No secrets. Add one to this project." : $"{choices.Length} secret key(s)."));
        UpdateActions();
    }

    private async Task ToggleRevealAsync()
    {
        if (disclosure is not null)
        {
            HideValue();
            UpdateActions();
            return;
        }
        if (!TrySelection(out ProjectChoice project, out SecretKeyChoice key))
        {
            return;
        }

        SetBusy(true, "Revealing selected value…");
        ProjectUserSecretRevealResult result = await service.RevealAsync(
            workspaceId, project.Project.Path, key.Key, cancellationToken);
        if (result.Outcome is ProjectUserSecretValueOutcome.Succeeded &&
            result.Disclosure is not null)
        {
            disclosure = result.Disclosure;
            value.PasswordChar = '\0';
            value.Text = disclosure.Value.Value;
            reveal.Content = "Hide";
            SetBusy(false, "Value revealed locally. Visual capture is blocked until it is hidden.");
        }
        else
        {
            SetBusy(false, result.Error ?? "The value could not be revealed.");
        }
        UpdateActions();
    }

    private async Task CopyAsync()
    {
        if (!TrySelection(out ProjectChoice project, out SecretKeyChoice key))
        {
            return;
        }
        SetBusy(true, "Copying selected value…");
        ProjectUserSecretCopyResult result = await service.CopyAsync(
            workspaceId, project.Project.Path, key.Key, cancellationToken);
        if (result.Outcome is ProjectUserSecretValueOutcome.Succeeded && result.Value is not null &&
            TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            try
            {
                await clipboard.SetTextAsync(result.Value.Value);
                SetBusy(false, "Secret value copied to the desktop clipboard.");
                return;
            }
            catch (Exception exception) when (exception is InvalidOperationException or
                                              NotSupportedException)
            {
                SetBusy(false, "The desktop clipboard is unavailable.");
                return;
            }
        }
        SetBusy(false, result.Error ?? "The value could not be copied.");
    }

    private async Task AddAsync()
    {
        if (projects.SelectedItem is not ProjectChoice project ||
            project.Project.State is not ProjectUserSecretsProjectState.Available)
        {
            return;
        }
        SecretEditInput? input = await new ProjectUserSecretEditorDialog(
            "Add project secret", key: null).ShowDialog<SecretEditInput?>(this);
        if (input is null)
        {
            return;
        }
        HideValue();
        SetBusy(true, "Adding secret…");
        ProjectUserSecretMutationResult result = await service.AddAsync(
            workspaceId, project.Project.Path, new(input.Key), new(input.Value), cancellationToken);
        await FinishMutationAsync(result, "Secret added.");
    }

    private async Task ChangeAsync()
    {
        if (!TrySelection(out ProjectChoice project, out SecretKeyChoice key))
        {
            return;
        }
        SecretEditInput? input = await new ProjectUserSecretEditorDialog(
            "Change project secret", key.Key.Value).ShowDialog<SecretEditInput?>(this);
        if (input is null)
        {
            return;
        }
        HideValue();
        SetBusy(true, "Changing secret…");
        ProjectUserSecretMutationResult result = await service.ChangeAsync(
            workspaceId, project.Project.Path, key.Key, new(input.Value), cancellationToken);
        await FinishMutationAsync(result, "Secret changed.");
    }

    private async Task DeleteAsync()
    {
        if (!TrySelection(out ProjectChoice project, out SecretKeyChoice key))
        {
            return;
        }
        bool confirmed = await new ProjectUserSecretDeleteDialog(key.Key.Value).ShowDialog<bool>(this);
        if (!confirmed)
        {
            return;
        }
        HideValue();
        SetBusy(true, "Deleting secret…");
        ProjectUserSecretMutationResult result = await service.DeleteAsync(
            workspaceId, project.Project.Path, key.Key, cancellationToken);
        await FinishMutationAsync(result, "Secret deleted.");
    }

    private async Task FinishMutationAsync(ProjectUserSecretMutationResult result, string success)
    {
        string message = result.Outcome is ProjectUserSecretMutationOutcome.Succeeded
            ? success
            : result.Error ?? "The project secret was not changed.";
        await RefreshSelectedAsync();
        status.Text = message;
    }

    private bool TrySelection(out ProjectChoice project, out SecretKeyChoice key)
    {
        project = (projects.SelectedItem as ProjectChoice)!;
        key = (keys.SelectedItem as SecretKeyChoice)!;
        return project is not null && key is not null;
    }

    private void HideValue()
    {
        disclosure?.Dispose();
        disclosure = null;
        value.Text = keys.SelectedItem is null ? "Select a secret key." : "••••••••";
        value.PasswordChar = '\0';
        reveal.Content = "Reveal";
    }

    private void SetBusy(bool isBusy, string message)
    {
        loading = isBusy;
        status.Text = message;
        UpdateActions();
    }

    private void UpdateActions()
    {
        bool projectAvailable = projects.SelectedItem is ProjectChoice
        {
            Project.State: ProjectUserSecretsProjectState.Available,
        };
        bool selected = projectAvailable && keys.SelectedItem is SecretKeyChoice;
        projects.IsEnabled = !loading && disclosure is null;
        keys.IsEnabled = !loading && disclosure is null;
        add.IsEnabled = !loading && projectAvailable && disclosure is null;
        change.IsEnabled = !loading && selected && disclosure is null;
        delete.IsEnabled = !loading && selected && disclosure is null;
        reveal.IsEnabled = !loading && (selected || disclosure is not null);
        copy.IsEnabled = !loading && selected;
    }

    private sealed record ProjectChoice(ProjectUserSecretsProjectView Project)
    {
        public override string ToString() => Project.Path.Value;
    }

    private sealed record SecretKeyChoice(ProjectUserSecretKey Key)
    {
        public override string ToString() => Key.Value;
    }

    private sealed record SecretEditInput(string Key, string Value)
    {
        public override string ToString() =>
            $"{nameof(SecretEditInput)} {{ Key = {Key}, Value = [REDACTED] }}";
    }

    private sealed class ProjectUserSecretEditorDialog : Window
    {
        private readonly TextBox key = new();
        private readonly TextBox value = new() { PasswordChar = '●' };
        private readonly Button save = new() { Content = "Save", IsEnabled = false };

        internal ProjectUserSecretEditorDialog(string title, string? key)
        {
            Title = title;
            Width = 560;
            Height = 300;
            MinWidth = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            this.key.Text = key ?? string.Empty;
            this.key.IsReadOnly = key is not null;
            this.key.PlaceholderText = "Configuration key, for example Services:ApiKey";
            value.PlaceholderText = key is null ? "Secret value" : "New secret value";
            AutomationProperties.SetName(this.key, "Project secret key");
            AutomationProperties.SetName(value, "Project secret value input");
            AutomationProperties.SetName(save, "Save project secret");
            Content = BuildContent();
            this.key.TextChanged += (_, _) => Validate();
            value.TextChanged += (_, _) => Validate();
            save.Click += (_, _) => Close(new SecretEditInput(
                this.key.Text ?? string.Empty, value.Text ?? string.Empty));
            Opened += (_, _) => _ = (key is null ? this.key : value).Focus();
            Validate();
        }

        private Control BuildContent()
        {
            Button cancel = new() { Content = "Cancel" };
            cancel.Click += (_, _) => Close(null);
            StackPanel actions = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { cancel, save },
            };
            save.Classes.Add("primary");
            Grid root = new()
            {
                RowDefinitions = new("Auto,Auto,Auto,Auto,Auto"),
                RowSpacing = 8,
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock { Text = "Key", FontWeight = FontWeight.SemiBold },
                },
            };
            Grid.SetRow(key, 1);
            root.Children.Add(key);
            TextBlock valueLabel = new() { Text = "Value", FontWeight = FontWeight.SemiBold };
            Grid.SetRow(valueLabel, 2);
            root.Children.Add(valueLabel);
            Grid.SetRow(value, 3);
            root.Children.Add(value);
            Grid.SetRow(actions, 4);
            root.Children.Add(actions);
            return root;
        }

        private void Validate()
        {
            string keyText = key.Text ?? string.Empty;
            save.IsEnabled = !string.IsNullOrWhiteSpace(keyText) &&
                             keyText.Equals(keyText.Trim(), StringComparison.Ordinal) &&
                             !keyText.Any(char.IsControl) &&
                             !keyText.StartsWith(':') && !keyText.EndsWith(':') &&
                             !keyText.Contains("::", StringComparison.Ordinal);
        }
    }

    private sealed class ProjectUserSecretDeleteDialog : Window
    {
        internal ProjectUserSecretDeleteDialog(string key)
        {
            Title = "Delete project secret";
            Width = 500;
            Height = 210;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Button cancel = new() { Content = "Cancel" };
            Button delete = new() { Content = "Delete" };
            delete.Classes.Add("danger");
            cancel.Click += (_, _) => Close(false);
            delete.Click += (_, _) => Close(true);
            StackPanel actions = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { cancel, delete },
            };
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Delete '{key}' from this project's User Secrets store?",
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 17,
                        FontWeight = FontWeight.SemiBold,
                    },
                    new TextBlock
                    {
                        Text = "This changes the per-user development store, not the repository. The value cannot be recovered by Harness.NET.",
                        TextWrapping = TextWrapping.Wrap,
                        Classes = { "muted" },
                    },
                    actions,
                },
            };
        }
    }
}
