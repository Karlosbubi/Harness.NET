using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Operations;
using Harness.BusinessLogic.Tools;
using Microsoft.Extensions.Logging;

namespace Harness.Presentation.Avalonia;

internal sealed partial class AvaloniaPresentationStore
{
    internal async ValueTask CreateApplicationBackupAsync(
        BackupDestinationPath destination,
        CancellationToken cancellationToken)
    {
        if (Current.Operations.IsBusy)
        {
            return;
        }

        Publish(Current with
        {
            Operations = Current.Operations with
            {
                IsBusy = true,
                LastBackup = null,
                Status = "Creating and integrity-checking application-state backup…",
            },
        });
        try
        {
            ApplicationBackupResult result = await applicationOperationsService.CreateBackupAsync(
                destination,
                cancellationToken);
            Publish(Current with
            {
                Operations = Current.Operations with
                {
                    LastBackup = result.Backup,
                    Status = result.Error ??
                             $"Verified backup created for schema {result.Backup?.SchemaVersion.Value}.",
                },
            });
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Application-state backup cancelled");
            Publish(Current with
            {
                Operations = Current.Operations with { Status = "Backup cancelled." },
            });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Application-state backup failed");
            Publish(Current with
            {
                Operations = Current.Operations with { Status = exception.Message },
            });
        }
        finally
        {
            Publish(Current with
            {
                Operations = Current.Operations with { IsBusy = false },
            });
        }
    }

    internal async ValueTask InspectApplicationRestoreAsync(
        RestoreSourcePath source,
        CancellationToken cancellationToken)
    {
        if (Current.Operations.IsBusy)
        {
            return;
        }

        Publish(Current with
        {
            Operations = Current.Operations with
            {
                IsBusy = true,
                InspectedRestore = null,
                Status = "Inspecting archive and verifying hashes, SQLite, schema, and layout…",
            }
        });
        try
        {
            ApplicationRestoreInspectionResult result =
                await applicationOperationsService.InspectRestoreAsync(source, cancellationToken);
            Publish(Current with
            {
                Operations = Current.Operations with
                {
                    InspectedRestore = result.Restore,
                    Status = result.Error ?? "Archive verified. Review it before staging restore.",
                }
            });
        }
        catch (OperationCanceledException)
        {
            Publish(Current with
            {
                Operations = Current.Operations with
                { Status = "Restore inspection cancelled." }
            });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Application-state restore inspection failed");
            Publish(Current with
            {
                Operations = Current.Operations with
                { Status = exception.Message }
            });
        }
        finally
        {
            Publish(Current with { Operations = Current.Operations with { IsBusy = false } });
        }
    }

    internal async ValueTask StageApplicationRestoreAsync(
        ApplicationRestoreView restore,
        CancellationToken cancellationToken)
    {
        if (Current.Operations.IsBusy)
        {
            return;
        }

        Publish(Current with
        {
            Operations = Current.Operations with
            { IsBusy = true, Status = "Revalidating and staging restore…" }
        });
        try
        {
            ApplicationRestoreStageResult result =
                await applicationOperationsService.StageRestoreAsync(
                    new(restore.Archive, restore.ArchiveSha256), cancellationToken);
            Publish(Current with
            {
                Operations = Current.Operations with
                {
                    PendingRestore = result.Restore,
                    Status = result.Error ?? (result.RestartRequired
                    ? "Verified restore staged. Restart Harness.NET to apply it."
                    : "Restore was not staged."),
                }
            });
        }
        catch (OperationCanceledException)
        {
            Publish(Current with
            {
                Operations = Current.Operations with
                { Status = "Restore staging cancelled." }
            });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Application-state restore staging failed");
            Publish(Current with
            {
                Operations = Current.Operations with
                { Status = exception.Message }
            });
        }
        finally
        {
            Publish(Current with { Operations = Current.Operations with { IsBusy = false } });
        }
    }

    internal async ValueTask RefreshCapabilityApprovalsAsync(
        GoalId goalId,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            CapabilityApprovalSnapshot result = await capabilityApprovalService.ListAsync(
                goalId.Value,
                cancellationToken);
            Publish(Current with
            {
                Goals = Current.Goals with
                {
                    CapabilityApprovals = result.Items,
                    Status = result.Error ?? $"{result.Items.Count} restore approval(s).",
                },
            });
        }, "Restore approval refresh");

    internal async ValueTask RequestRestoreApprovalAsync(
        GoalId goalId,
        ToolCorrelationId correlationId,
        string rationale,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            CapabilityApprovalResult result = await capabilityApprovalService.RequestAsync(new(
                goalId.Value,
                correlationId,
                CapabilityKind.Restore,
                rationale), cancellationToken);
            if (result.Approval is null)
            {
                PublishGoalStatus(result.Error ?? "Restore approval request failed.");
                return;
            }

            await ReloadCapabilityApprovalsAsync(
                goalId,
                "Restore request recorded as Pending. A separate decision is required.",
                cancellationToken);
        }, "Restore approval request");

    internal async ValueTask DecideRestoreApprovalAsync(
        GoalId goalId,
        CapabilityApprovalId approvalId,
        CapabilityDecision decision,
        string? reason,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            CapabilityApprovalResult result = await capabilityApprovalService.DecideAsync(new(
                approvalId,
                decision,
                reason), cancellationToken);
            if (result.Approval is null)
            {
                PublishGoalStatus(result.Error ?? "Restore approval decision failed.");
                return;
            }

            await ReloadCapabilityApprovalsAsync(
                goalId,
                decision is CapabilityDecision.Approve
                    ? "Approved exactly one correlated restore request."
                    : "Restore request denied.",
                cancellationToken);
        }, "Restore approval decision");

    public void Dispose()
    {
        submission?.Cancel();
        submission?.Dispose();
        workflowExecution?.Cancel();
        workflowExecution?.Dispose();
        semanticExecution?.Cancel();
        semanticExecution?.Dispose();
        commandGate.Dispose();
        states.Dispose();
    }

}
