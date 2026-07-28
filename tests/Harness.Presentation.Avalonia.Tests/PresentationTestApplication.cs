using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Dock.Avalonia.Themes.Fluent;

namespace Harness.Presentation.Avalonia.Tests;

public sealed class PresentationTestApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new DockFluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Harness.Presentation.Avalonia.Tests/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"),
        });
    }
}
