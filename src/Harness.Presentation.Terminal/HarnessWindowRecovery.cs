using System.Collections.ObjectModel;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Dashboard;
using Harness.BusinessLogic.Framework;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Operations;
using Harness.BusinessLogic.Retrieval;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Terminal.Gui.App;
using Terminal.Gui.Editor;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Harness.Presentation.Terminal;

internal sealed partial class HarnessWindow
{
    private async Task CreateBackupAsync()
    {
        try
        {
            using Dialog destinationDialog = new()
            {
                Title = "Export Harness.NET application state",
                Width = Dim.Percent(80),
                Height = 11,
            };
            destinationDialog.Add(new Label
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Text = "New absolute .zip destination (existing files are never overwritten)",
            });
            TextField destination = new()
            {
                X = 0,
                Y = 1,
                Width = Dim.Fill(),
            };
            destinationDialog.Add(destination, new Label
            {
                X = 0,
                Y = 3,
                Width = Dim.Fill(),
                Height = 4,
                Text = "The archive contains private prompts, workflow evidence, approvals, " +
                       "costs, and semantic state. It excludes credentials, logs, caches, " +
                       "worktrees, and user repositories.",
            });
            BackupDestinationPath? selected = null;
            Button continueButton = new() { Title = "_Continue" };
            continueButton.Accepting += (_, args) =>
            {
                args.Handled = true;
                string value = destination.Text?.ToString()?.Trim() ?? string.Empty;
                if (value.Length == 0)
                {
                    return;
                }

                selected = new(value);
                destinationDialog.RequestStop();
            };
            destinationDialog.AddButton(continueButton);
            destinationDialog.AddButton(new Button { Title = "_Cancel" });
            await application.RunAsync(destinationDialog, cancellationToken);
            if (selected is null)
            {
                return;
            }

            int? confirmation = MessageBox.Query(
                application,
                "Confirm private-state export",
                $"Create a sensitive application-state backup at:\n{selected.Value}\n\n" +
                "Protect this archive like the original Harness.NET data directory.",
                "_Create",
                "_Cancel");
            if (confirmation != 0)
            {
                return;
            }

            status.Text = "Creating and integrity-checking application-state backup...";
            ApplicationBackupResult result = await operationsService.CreateBackupAsync(
                selected, cancellationToken);
            if (result.Backup is null)
            {
                status.Text = result.Error ?? "Backup failed.";
                return;
            }

            using Dialog completed = new()
            {
                Title = "Verified backup created",
                Width = Dim.Percent(85),
                Height = Dim.Percent(70),
            };
            completed.Add(new Editor
            {
                Text = ApplicationBackupTextFormatter.Format(result.Backup),
                ReadOnly = true,
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar |
                                   ViewportSettingsFlags.HasHorizontalScrollBar,
            });
            completed.AddButton(new Button { Title = "_Close" });
            await application.RunAsync(completed, cancellationToken);
            status.Text = $"Verified backup created | schema {result.Backup.SchemaVersion.Value}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            application.Invoke(() => status.Text = $"Backup failed | {exception.Message}");
        }
    }

    private async Task RestoreBackupAsync()
    {
        try
        {
            using Dialog sourceDialog = new()
            {
                Title = "Inspect application-state backup",
                Width = Dim.Percent(85),
                Height = 12,
            };
            sourceDialog.Add(new Label
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Text = "Absolute path to an existing Harness.NET .zip backup",
            });
            TextField source = new() { X = 0, Y = 1, Width = Dim.Fill() };
            sourceDialog.Add(source, new Label
            {
                X = 0,
                Y = 3,
                Width = Dim.Fill(),
                Height = 4,
                Text = "Restore replaces private prompts, settings, approvals, costs, index " +
                       "state, and layout. It does not restore credentials or repositories.",
            });
            RestoreSourcePath? selected = null;
            Button inspect = new() { Title = "_Inspect" };
            inspect.Accepting += (_, args) =>
            {
                args.Handled = true;
                string value = source.Text?.ToString()?.Trim() ?? string.Empty;
                if (value.Length > 0)
                {
                    selected = new(value);
                    sourceDialog.RequestStop();
                }
            };
            sourceDialog.AddButton(inspect);
            sourceDialog.AddButton(new Button { Title = "_Cancel" });
            await application.RunAsync(sourceDialog, cancellationToken);
            if (selected is null)
            {
                return;
            }

            status.Text = "Inspecting and verifying restore archive...";
            ApplicationRestoreInspectionResult inspected =
                await operationsService.InspectRestoreAsync(selected, cancellationToken);
            if (inspected.Restore is null)
            {
                status.Text = inspected.Error ?? "Restore inspection failed.";
                return;
            }

            int? confirmation = MessageBox.Query(
                application,
                "Confirm private-state restore",
                ApplicationRestoreTextFormatter.Format(inspected.Restore) +
                "\n\nChanges made after staging will be replaced on restart. The archive " +
                "is revalidated before replacement and current state is retained for rollback.",
                "_Stage for restart",
                "_Cancel");
            if (confirmation != 0)
            {
                status.Text = "Restore not staged.";
                return;
            }

            ApplicationRestoreStageResult staged =
                await operationsService.StageRestoreAsync(
                    new(selected, inspected.Restore.ArchiveSha256), cancellationToken);
            status.Text = staged.Restore is null
                ? staged.Error ?? "Restore staging failed."
                : "Verified restore staged | restart Harness.NET to apply it";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            application.Invoke(() => status.Text = $"Restore failed | {exception.Message}");
        }
    }

}
