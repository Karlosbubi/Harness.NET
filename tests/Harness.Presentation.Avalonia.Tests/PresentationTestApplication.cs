using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Dock.Avalonia.Themes.Fluent;

namespace Harness.Presentation.Avalonia.Tests;

public class PresentationTestApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new DockFluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Harness.Presentation.Avalonia.Tests/"))
        {
            Source = new Uri("avares://Harness.Presentation.Avalonia/WorkbenchStyles.axaml"),
        });
        Styles.Add(new StyleInclude(new Uri("avares://Harness.Presentation.Avalonia.Tests/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"),
        });
        Styles.Add(new StyleInclude(new Uri("avares://Harness.Presentation.Avalonia.Tests/"))
        {
            Source = new Uri("avares://SvcSystems.UI.Terminal/Styles/Colors.axaml"),
        });
    }
}

public static class RenderingTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<PresentationTestApplication>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
                ShouldRenderOnUIThread = true,
            });
}

// Avalonia's StartNew implementation captures its dispatch task through a racy local;
// creating a session per test makes a null dispatch task likely across this large suite.
// One shared dispatcher still gives every Dispatch call PerTest application isolation.
internal sealed class HeadlessUnitTestSession : IDisposable
{
    private static readonly Lazy<global::Avalonia.Headless.HeadlessUnitTestSession> Shared = new(
        () => global::Avalonia.Headless.HeadlessUnitTestSession.StartNew(
            typeof(RenderingTestAppBuilder)));
    private readonly global::Avalonia.Headless.HeadlessUnitTestSession session = Shared.Value;

    private HeadlessUnitTestSession()
    {
    }

    public static HeadlessUnitTestSession StartNew(Type entryPointType)
    {
        if (entryPointType != typeof(RenderingTestAppBuilder))
        {
            throw new ArgumentException("The shared session requires the rendering test app.",
                nameof(entryPointType));
        }

        return new();
    }

    public Task Dispatch(Action action, CancellationToken cancellationToken) =>
        session.Dispatch(action, cancellationToken);

    public Task<TResult> Dispatch<TResult>(
        Func<TResult> action,
        CancellationToken cancellationToken) => session.Dispatch(action, cancellationToken);

    public Task<TResult> Dispatch<TResult>(
        Func<Task<TResult>> action,
        CancellationToken cancellationToken) => session.Dispatch(action, cancellationToken);

    public void Dispose()
    {
        // The shared dispatcher lives for the test process. Dispatch itself tears down
        // the isolated application and Avalonia locator scope after every test body.
    }
}
