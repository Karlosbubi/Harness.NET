using System.Collections.ObjectModel;
using Harness.BusinessLogic.Dashboard;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Harness.Presentation.Terminal;

internal sealed class HarnessWindow : Window
{
    private const int WorkspaceWidth = 26;
    private const int ComposerHeight = 3;
    private const int FooterHeight = 1;

    private readonly IApplication application;
    private readonly IDashboardService dashboardService;
    private readonly CancellationToken cancellationToken;
    private readonly FrameView workspaceFrame;
    private readonly FrameView activityFrame;
    private readonly FrameView detailsFrame;
    private readonly Label workspaceText;
    private readonly Label activityText;
    private readonly Label detailsText;
    private readonly ListView modelList;
    private readonly Button refreshModels;
    private readonly Button useModel;
    private readonly TextField composer;
    private readonly Button send;
    private readonly Label status;
    private string[] availableModelIds = [];

    internal HarnessWindow(
        IApplication application,
        IDashboardService dashboardService,
        DashboardSnapshot initialSnapshot,
        CancellationToken cancellationToken)
    {
        this.application = application;
        this.dashboardService = dashboardService;
        this.cancellationToken = cancellationToken;
        Title = "Harness.NET";

        workspaceText = CreateContentLabel();
        activityText = CreateContentLabel();
        detailsText = CreateContentLabel();

        workspaceFrame = CreateFrame("Workspace", workspaceText);
        activityFrame = CreateFrame("Activity", activityText);
        detailsFrame = CreateFrame("Plan | Diff | Evidence", detailsText);
        detailsText.Height = Dim.Fill(7);
        modelList = new ListView
        {
            X = 0,
            Y = Pos.AnchorEnd(6),
            Width = Dim.Fill(),
            Height = 3,
        };
        refreshModels = new Button
        {
            Title = "_Refresh",
            X = 0,
            Y = Pos.AnchorEnd(2),
            Width = 12,
        };
        useModel = new Button
        {
            Title = "_Use model",
            X = Pos.Right(refreshModels) + 1,
            Y = Pos.AnchorEnd(2),
            Width = 14,
        };
        refreshModels.Accepted += async (_, _) => await RefreshProviderAsync();
        useModel.Accepted += async (_, _) => await SelectModelAsync();
        detailsFrame.Add(modelList, refreshModels, useModel);

        composer = new TextField
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(12),
            Height = 1,
        };
        composer.Accepted += async (_, _) => await SubmitAsync();
        send = new()
        {
            Title = "_Send",
            X = Pos.AnchorEnd(10),
            Y = 0,
            Width = 10,
        };
        send.Accepted += async (_, _) => await SubmitAsync();

        FrameView composerFrame = new()
        {
            Title = "Instruction",
            X = 0,
            Y = Pos.AnchorEnd(ComposerHeight + FooterHeight),
            Width = Dim.Fill(),
            Height = ComposerHeight,
        };
        composerFrame.Add(composer, send);

        status = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(FooterHeight),
            Width = Dim.Fill(),
            Height = FooterHeight,
        };

        Add(workspaceFrame, activityFrame, detailsFrame, composerFrame, status);
        Render(initialSnapshot);
        ViewportChanged += (_, _) => ApplyLayout(Viewport.Width);
        Initialized += (_, _) => composer.SetFocus();
    }

    private async Task SubmitAsync()
    {
        string instruction = composer.Text?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(instruction))
        {
            return;
        }

        try
        {
            send.Enabled = false;
            composer.ReadOnly = true;
            await foreach (DashboardSnapshot snapshot in dashboardService
                               .SubmitAsync(instruction, cancellationToken)
                               .WithCancellation(cancellationToken))
            {
                application.Invoke(() => Render(snapshot));
            }

            application.Invoke(() => composer.Text = string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            application.Invoke(() => status.Text = $"Submission failed | {exception.Message}");
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                application.Invoke(() =>
                {
                    send.Enabled = true;
                    composer.ReadOnly = false;
                    composer.SetFocus();
                });
            }
        }
    }

    private void Render(DashboardSnapshot snapshot)
    {
        workspaceText.Text = string.Join('\n',
            snapshot.Workspace.Name,
            snapshot.Workspace.Path,
            string.Empty,
            $"Branch  {snapshot.Workspace.Branch}",
            $"Trust   {snapshot.Workspace.Trust}",
            string.Empty,
            snapshot.Goal);

        activityText.Text = string.Join(
            "\n\n",
            snapshot.Activities.Select(item =>
                $"{item.Actor} [{item.Status}]\n{item.Summary}"));
        int transcriptLines = activityText.Text?.ToString()?.Count(character => character == '\n') + 1 ?? 1;
        activityText.SetContentHeight(transcriptLines);
        activityText.VerticalScrollBar.Value = Math.Max(0, transcriptLines - activityText.Viewport.Height);

        detailsText.Text = string.Join('\n', snapshot.Plan) +
                           $"\n\nDIFF\n{snapshot.Diff}\n\nEVIDENCE\n" +
                           string.Join(
                               '\n',
                               snapshot.Evidence.Select(item => $"{item.Title}: {item.Content}")) +
                           $"\n\nPROVIDER\n{snapshot.Provider.Name}: {snapshot.Provider.Health}" +
                           (snapshot.Provider.Error is null
                               ? string.Empty
                               : $"\n{snapshot.Provider.Error}");

        availableModelIds = snapshot.Provider.Models.Select(model => model.Id).ToArray();
        string[] modelLabels = snapshot.Provider.Models.Select(model =>
            $"{(model.Id == snapshot.Provider.SelectedModel ? "*" : " ")} {model.Id}  " +
            string.Join(',', model.Capabilities)).ToArray();
        ObservableCollection<string> models = new(modelLabels);
        modelList.SetSource(models);
        int selectedModel = Array.IndexOf(availableModelIds, snapshot.Provider.SelectedModel);
        if (selectedModel >= 0)
        {
            modelList.SelectedItem = selectedModel;
        }

        status.Text = $"{snapshot.Status} | {snapshot.Budget}";
    }

    private async Task RefreshProviderAsync() => await RunProviderCommandAsync(
        token => dashboardService.RefreshProviderAsync(token));

    private async Task SelectModelAsync()
    {
        int selected = modelList.SelectedItem ?? -1;
        if (selected < 0 || selected >= availableModelIds.Length)
        {
            status.Text = "No model selected";
            return;
        }

        string model = availableModelIds[selected];
        await RunProviderCommandAsync(token => dashboardService.SelectModelAsync(model, token));
    }

    private async Task RunProviderCommandAsync(
        Func<CancellationToken, ValueTask<DashboardSnapshot>> command)
    {
        try
        {
            refreshModels.Enabled = false;
            useModel.Enabled = false;
            DashboardSnapshot snapshot = await command(cancellationToken);
            application.Invoke(() => Render(snapshot));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            application.Invoke(() => status.Text = $"Provider command failed | {exception.Message}");
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                application.Invoke(() =>
                {
                    refreshModels.Enabled = true;
                    useModel.Enabled = true;
                });
            }
        }
    }

    private void ApplyLayout(int width)
    {
        ShellLayout layout = ShellLayoutPolicy.ForWidth(width);
        workspaceFrame.Visible = layout.ShowWorkspace;
        detailsFrame.Visible = layout.ShowDetails;

        workspaceFrame.X = 0;
        workspaceFrame.Width = WorkspaceWidth;

        activityFrame.X = layout.ShowWorkspace ? Pos.Right(workspaceFrame) : 0;
        activityFrame.Width = layout.ShowDetails ? Dim.Percent(50) - WorkspaceWidth : Dim.Fill();

        detailsFrame.X = Pos.Right(activityFrame);
        detailsFrame.Width = Dim.Fill();
    }

    private static FrameView CreateFrame(string title, View content)
    {
        FrameView frame = new()
        {
            Title = title,
            Y = 0,
            Height = Dim.Fill(ComposerHeight + FooterHeight),
        };
        frame.Add(content);
        return frame;
    }

    private static Label CreateContentLabel() => new()
    {
        X = 0,
        Y = 0,
        Width = Dim.Fill(),
        Height = Dim.Fill(),
        CanFocus = true,
        ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar,
    };
}
