using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Harness.Presentation.Avalonia;

/// <summary>
/// Finds and runs a real application command by name. Unavailable commands stay listed
/// with the reason they cannot run, so the palette never hides a capability or pretends
/// one exists.
/// </summary>
internal sealed class CommandPaletteDialog : Window
{
    private readonly IReadOnlyList<PaletteCommand> commands;
    private readonly TextBox query = new();
    private readonly ListBox results = new() { Classes = { "palette-results" } };

    internal CommandPaletteDialog(
        IReadOnlyList<PaletteCommand> commands,
        string title = "Run a command",
        string placeholder = "Type a command")
    {
        this.commands = commands;
        query.PlaceholderText = placeholder;
        Title = title;
        Classes.Add("palette");
        Width = 640;
        Height = 460;
        CanResize = false;
        WindowDecorations = WindowDecorations.BorderOnly;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AutomationProperties.SetName(query, "Command palette filter");
        AutomationProperties.SetName(results, "Command palette results");

        results.ItemTemplate = new FuncDataTemplate<PaletteCommand>((command, _) =>
            Row(command), supportsRecycling: true);

        Grid root = new()
        {
            RowDefinitions = new("Auto,*,Auto"),
            Margin = new Thickness(14),
            RowSpacing = 10,
        };
        root.Children.Add(query);
        results.SetValue(Grid.RowProperty, 1);
        root.Children.Add(results);

        TextBlock hint = new()
        {
            Classes = { "muted" },
            Text = "↑↓ to choose · Enter to run · Esc to dismiss",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        hint.SetValue(Grid.RowProperty, 2);
        root.Children.Add(hint);
        Content = root;

        query.GetObservable(TextBox.TextProperty).Subscribe(_ => Apply());
        query.KeyDown += OnQueryKeyDown;
        results.DoubleTapped += async (_, _) => await RunSelectedAsync();
        KeyDown += OnPaletteKeyDown;
        Opened += (_, _) => query.Focus();
    }

    private Control Row(PaletteCommand command)
    {
        StackPanel text = new()
        {
            Spacing = 1,
            Children =
            {
                new TextBlock { Text = command.Title, FontWeight = FontWeight.SemiBold },
                new TextBlock
                {
                    Classes = { "muted" },
                    Text = command.UnavailableReason ?? command.Category,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };

        Grid row = new() { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(2) };
        row.Children.Add(text);
        if (command.Shortcut is { Length: > 0 } shortcut)
        {
            Border chip = new()
            {
                Classes = { "chip" },
                Child = new TextBlock { Text = shortcut },
            };
            chip.SetValue(Grid.ColumnProperty, 1);
            row.Children.Add(chip);
        }

        Button action = new()
        {
            Content = row,
            IsEnabled = command.IsAvailable,
            Opacity = command.IsAvailable ? 1 : 0.55,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        action.Classes.Add("palette-command");
        AutomationProperties.SetName(action, command.Title);
        action.Click += async (_, _) =>
        {
            Close();
            await command.InvokeAsync();
        };
        return action;
    }

    private void Apply()
    {
        IReadOnlyList<PaletteCommand> ranked =
            CommandPaletteFilter.Rank(commands, query.Text ?? string.Empty);
        results.ItemsSource = ranked;
        results.SelectedItem = ranked.FirstOrDefault(command => command.IsAvailable)
                               ?? ranked.FirstOrDefault();
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs args)
    {
        // Arrow keys steer the result list while focus stays in the filter box.
        if (args.Key is Key.Down or Key.Up)
        {
            Move(args.Key is Key.Down ? 1 : -1);
            args.Handled = true;
        }
    }

    private async void OnPaletteKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key is Key.Escape)
        {
            args.Handled = true;
            Close();
        }
        else if (args.Key is Key.Enter)
        {
            args.Handled = true;
            await RunSelectedAsync();
        }
    }

    private void Move(int delta)
    {
        int count = results.ItemCount;
        if (count == 0)
        {
            return;
        }

        int next = results.SelectedIndex + delta;
        results.SelectedIndex = ((next % count) + count) % count;
        results.ScrollIntoView(results.SelectedIndex);
    }

    private async ValueTask RunSelectedAsync()
    {
        if (results.SelectedItem is not PaletteCommand { IsAvailable: true } command)
        {
            return;
        }

        Close();
        await command.InvokeAsync();
    }
}
