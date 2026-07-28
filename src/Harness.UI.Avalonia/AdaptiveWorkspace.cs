using Avalonia;
using Avalonia.Controls;

namespace Harness.UI.Avalonia;

public sealed class AdaptiveWorkspace : Grid
{
    public static readonly StyledProperty<Control?> NavigationProperty =
        AvaloniaProperty.Register<AdaptiveWorkspace, Control?>(nameof(Navigation));
    public static readonly StyledProperty<Control?> PrimaryProperty =
        AvaloniaProperty.Register<AdaptiveWorkspace, Control?>(nameof(Primary));
    public static readonly StyledProperty<Control?> UtilityProperty =
        AvaloniaProperty.Register<AdaptiveWorkspace, Control?>(nameof(Utility));

    private readonly ColumnDefinition navigationColumn = new(248, GridUnitType.Pixel);
    private readonly ColumnDefinition primaryColumn = new(1, GridUnitType.Star);
    private readonly ColumnDefinition utilityColumn = new(300, GridUnitType.Pixel);

    static AdaptiveWorkspace()
    {
        NavigationProperty.Changed.AddClassHandler<AdaptiveWorkspace>((workspace, _) => workspace.Rebuild());
        PrimaryProperty.Changed.AddClassHandler<AdaptiveWorkspace>((workspace, _) => workspace.Rebuild());
        UtilityProperty.Changed.AddClassHandler<AdaptiveWorkspace>((workspace, _) => workspace.Rebuild());
    }

    public AdaptiveWorkspace()
    {
        ColumnDefinitions.Add(navigationColumn);
        ColumnDefinitions.Add(primaryColumn);
        ColumnDefinitions.Add(utilityColumn);
        SizeChanged += (_, _) => UpdateMode();
    }

    public Control? Navigation
    {
        get => GetValue(NavigationProperty);
        set => SetValue(NavigationProperty, value);
    }

    public Control? Primary
    {
        get => GetValue(PrimaryProperty);
        set => SetValue(PrimaryProperty, value);
    }

    public Control? Utility
    {
        get => GetValue(UtilityProperty);
        set => SetValue(UtilityProperty, value);
    }

    private void Rebuild()
    {
        Children.Clear();
        Add(Navigation, 0);
        Add(Primary, 1);
        Add(Utility, 2);
        UpdateMode();
    }

    private void Add(Control? control, int column)
    {
        if (control is null)
        {
            return;
        }

        SetColumn(control, column);
        Children.Add(control);
    }

    private void UpdateMode()
    {
        double width = Bounds.Width;
        bool narrow = width > 0 && width < 1080;
        bool compact = width >= 1080 && width < 1260;
        navigationColumn.Width = new(narrow ? 56 : compact ? 220 : 248, GridUnitType.Pixel);
        utilityColumn.Width = new(narrow ? 0 : compact ? 250 : 300, GridUnitType.Pixel);
        if (Utility is not null)
        {
            Utility.IsVisible = !narrow;
        }
    }
}
