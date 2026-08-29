using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Agents;
using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.Costs;
using Harness.BusinessLogic.Events;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Retrieval;
using Harness.BusinessLogic.Workflows;
using Harness.BusinessLogic.Workspaces;
using Microsoft.Extensions.Logging;

namespace Harness.Presentation.Avalonia;

internal sealed partial class AvaloniaPresentationStore
{
    internal async ValueTask RefreshGoalsAsync(CancellationToken cancellationToken)
    {
        await RunGoalCommandAsync(async () =>
        {
            IReadOnlyList<GoalView> goals = await LoadGoalsAsync(
                Current.Workspaces.Registered,
                cancellationToken);
            GoalId? selectedId = Current.Goals.SelectedGoalId;
            GoalView? selected = selectedId is null
                ? null
                : goals.FirstOrDefault(goal => goal.Id == selectedId);
            PlanView? plan = selected is null
                ? null
                : await goalService.GetCurrentPlanAsync(selected.Id, cancellationToken);
            GoalDetails details = selected is null
                ? GoalDetails.Empty
                : await LoadGoalDetailsAsync(selected.Id, cancellationToken);
            Publish(Current with
            {
                Goals = Current.Goals with
                {
                    Items = goals,
                    SelectedGoalId = selected?.Id,
                    CurrentPlan = plan,
                    ModelSelections = details.Selections,
                    Cost = details.Cost,
                    Workflow = details.Workflow,
                    CommitApproval = details.CommitApproval,
                    CapabilityApprovals = details.CapabilityApprovals,
                    Status = goals.Count == 0 ? "Create the first goal." : $"{goals.Count} goal(s).",
                },
            });
        }, "Goal refresh");
    }

    internal async ValueTask SelectGoalAsync(
        GoalId goalId,
        CancellationToken cancellationToken)
    {
        if (!Current.Goals.Items.Any(goal => goal.Id == goalId))
        {
            return;
        }

        await RunGoalCommandAsync(async () =>
        {
            GoalDetails details = await LoadGoalDetailsAsync(goalId, cancellationToken);
            WorkspaceView? active = ActiveWorkspace(Current.Workspaces.Registered);
            if (active is not null)
            {
                selectedGoalsByWorkspace[active.Id] = goalId;
            }

            Publish(Current with
            {
                Goals = Current.Goals with
                {
                    SelectedGoalId = goalId,
                    CurrentPlan = details.Plan,
                    ModelCatalog = null,
                    ModelSelections = details.Selections,
                    Cost = details.Cost,
                    Workflow = details.Workflow,
                    SemanticStatus = null,
                    SemanticRebuild = null,
                    SemanticSearch = null,
                    CommitPreview = null,
                    CommitApproval = details.CommitApproval,
                    CapabilityApprovals = details.CapabilityApprovals,
                    Status = null,
                },
            });
        }, "Goal selection");
    }

    internal async ValueTask CreateGoalAsync(
        GoalCreateRequest request,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            GoalResult result = await goalService.CreateAsync(request, cancellationToken);
            if (result.Goal is null)
            {
                PublishGoalStatus(result.Error ?? "Goal creation failed.");
                return;
            }

            await ReloadGoalsAsync(
                result.Goal.Id,
                $"Created '{result.Goal.Title}'.",
                cancellationToken);
        }, "Goal creation");

    internal async ValueTask UpdateGoalSettingsAsync(
        GoalSettingsUpdateRequest request,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            GoalResult result = await goalService.UpdateSettingsAsync(request, cancellationToken);
            if (result.Goal is null)
            {
                PublishGoalStatus(result.Error ?? "Goal settings update failed.");
                return;
            }

            await ReloadGoalsAsync(
                result.Goal.Id,
                RemoteSpendPreference.FromGoalBudget(result.Goal.RemoteBudget).Mode switch
                {
                    RemoteSpendMode.Unlimited => "Saved private goal limits with unlimited remote spending.",
                    RemoteSpendMode.Capped => $"Saved explicit remote cap of ${GoalPresentationFormatter.ToUsd(result.Goal.RemoteBudget!.Value)}.",
                    _ => "Saved private goal limits with remote spending disabled.",
                },
                cancellationToken);
        }, "Goal settings update");

    internal async ValueTask ExtendGoalBudgetAsync(
        GoalBudgetExtensionRequest request,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            GoalBudgetExtensionResult result = await goalService.ExtendRemoteBudgetAsync(
                request, cancellationToken);
            if (result.Goal is null || result.Extension is null)
            {
                PublishGoalStatus(result.Error ?? "Remote budget extension failed.");
                return;
            }

            await ReloadGoalsAsync(
                result.Goal.Id,
                $"Increased the explicit remote cap to $" +
                $"{GoalPresentationFormatter.ToUsd(result.Extension.NewBudget.Value)}. " +
                "The extension is durable and does not retry a model call automatically.",
                cancellationToken);
        }, "Remote budget extension");

    internal async ValueTask ProposePlanAsync(
        GoalId goalId,
        string content,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            PlanResult result = await goalService.ProposePlanAsync(
                new(goalId, content),
                cancellationToken);
            if (result.Plan is null)
            {
                PublishGoalStatus(result.Error ?? "Plan proposal failed.");
                return;
            }

            await ReloadGoalsAsync(
                goalId,
                $"Plan revision {result.Plan.Revision.Value} awaits approval.",
                cancellationToken);
        }, "Plan proposal");

    internal async ValueTask DecidePlanAsync(
        GoalId goalId,
        PlanDecision decision,
        string? reason,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            PlanView? plan = await goalService.GetCurrentPlanAsync(goalId, cancellationToken);
            if (plan is null)
            {
                PublishGoalStatus("The selected goal has no current plan.");
                return;
            }

            PlanResult result = await goalService.DecidePlanAsync(
                new(goalId, plan.Id, decision, reason),
                cancellationToken);
            if (result.Goal is null)
            {
                PublishGoalStatus(result.Error ?? "Plan decision failed.");
                return;
            }

            string status = decision is PlanDecision.Approve
                ? $"Approved. Isolated branch: {result.Worktree?.Branch}"
                : "Denied. A revised plan is required.";
            await ReloadGoalsAsync(goalId, status, cancellationToken);
        }, "Plan decision");

    internal async ValueTask DiscoverGoalModelsAsync(
        GoalId goalId,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            GoalModelCatalog catalog = await goalModelService.DiscoverAsync(
                goalId,
                cancellationToken);
            IReadOnlyList<GoalModelSelectionView> selections =
                await goalModelService.GetSelectionsAsync(goalId, cancellationToken);
            Publish(Current with
            {
                Goals = Current.Goals with
                {
                    ModelCatalog = catalog,
                    ModelSelections = selections,
                    Status = catalog.Error ?? CatalogStatus(catalog),
                },
            });
        }, "Goal model discovery");

    internal async ValueTask SelectGoalModelAsync(
        GoalId goalId,
        AgentRole role,
        GoalModelCandidate candidate,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            GoalModelSelectionResult result = await goalModelService.SelectAsync(new(
                goalId,
                role,
                candidate.Provider,
                candidate.Model), cancellationToken);
            if (result.Selection is null)
            {
                PublishGoalStatus(result.Error ?? "Model selection failed.");
                return;
            }

            IReadOnlyList<GoalModelSelectionView> selections =
                await goalModelService.GetSelectionsAsync(goalId, cancellationToken);
            RemoteCostReport? cost = await remoteCostService.GetAsync(goalId, cancellationToken);
            Publish(Current with
            {
                Goals = Current.Goals with
                {
                    ModelSelections = selections,
                    Cost = cost,
                    Status = $"Selected {candidate.Provider.Value}/{candidate.Model.Value} for {role}.",
                },
            });
        }, "Goal model selection");

    internal async ValueTask StartGoalWorkflowAsync(
        GoalId goalId,
        GoalModelCandidate leadModel,
        CancellationToken cancellationToken) =>
        await RunWorkflowAsync(
            goalId,
            token => StartPlanningWithModelAsync(goalId, leadModel, token),
            cancellationToken,
            "Lead planning");

    private async IAsyncEnumerable<GoalWorkflowSnapshot> StartPlanningWithModelAsync(
        GoalId goalId,
        GoalModelCandidate leadModel,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        GoalModelSelectionResult selected = await goalModelService.SelectAsync(new(
            goalId,
            AgentRole.Lead,
            leadModel.Provider,
            leadModel.Model), cancellationToken);
        if (selected.Selection is null)
        {
            throw new InvalidOperationException(selected.Error ?? "Lead model selection failed.");
        }

        IReadOnlyList<GoalModelSelectionView> selections =
            await goalModelService.GetSelectionsAsync(goalId, cancellationToken);
        Publish(Current with
        {
            Goals = Current.Goals with { ModelSelections = selections },
        });
        await foreach (GoalWorkflowSnapshot snapshot in goalWorkflowService.StartPlanningAsync(
                           new(goalId), cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            yield return snapshot;
        }
    }

    internal async ValueTask ResumeGoalWorkflowAsync(
        GoalId goalId,
        CancellationToken cancellationToken) =>
        await RunWorkflowAsync(
            goalId,
            token => goalWorkflowService.ResumeAsync(
                new(goalId),
                token),
            cancellationToken,
            "Production workflow");

    internal async ValueTask RetryGoalWorkflowAsync(
        GoalId goalId,
        GoalWorkflowRetryRole role,
        GoalModelCandidate model,
        GoalRetryGuidance? guidance,
        CancellationToken cancellationToken) =>
        await RunWorkflowAsync(
            goalId,
            token => RetryWithModelAsync(
                goalId, role, model, guidance, token),
            cancellationToken,
            $"{role} retry");

    private async IAsyncEnumerable<GoalWorkflowSnapshot> RetryWithModelAsync(
        GoalId goalId,
        GoalWorkflowRetryRole retryRole,
        GoalModelCandidate model,
        GoalRetryGuidance? guidance,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        AgentRole role = retryRole switch
        {
            GoalWorkflowRetryRole.Lead => AgentRole.Lead,
            GoalWorkflowRetryRole.Implementer => AgentRole.Implementer,
            GoalWorkflowRetryRole.Reviewer => AgentRole.Reviewer,
            _ => throw new ArgumentOutOfRangeException(nameof(retryRole)),
        };
        GoalModelSelectionResult selected = await goalModelService.SelectAsync(new(
            goalId,
            role,
            model.Provider,
            model.Model), cancellationToken);
        if (selected.Selection is null)
        {
            throw new InvalidOperationException(selected.Error ?? "Retry model selection failed.");
        }

        IReadOnlyList<GoalModelSelectionView> selections =
            await goalModelService.GetSelectionsAsync(goalId, cancellationToken);
        Publish(Current with
        {
            Goals = Current.Goals with { ModelSelections = selections },
        });
        await foreach (GoalWorkflowSnapshot snapshot in goalWorkflowService.RetryAsync(
                           new(goalId, retryRole, guidance),
                           cancellationToken).WithCancellation(cancellationToken))
        {
            yield return snapshot;
        }
    }

    internal async ValueTask AbortGoalAsync(
        GoalId goalId,
        GoalAbortReason reason,
        CancellationToken cancellationToken)
    {
        await RunGoalCommandAsync(async () =>
        {
            await goalWorkflowService.AbortAsync(new(goalId, reason), cancellationToken);
            WorkspaceView? active = ActiveWorkspace(Current.Workspaces.Registered);
            if (active is not null)
            {
                selectedGoalsByWorkspace.Remove(active.Id);
            }

            Publish(Current with
            {
                Goals = Current.Goals with
                {
                    Items = Current.Goals.Items.Where(goal => goal.Id != goalId).ToArray(),
                    SelectedGoalId = null,
                    CurrentPlan = null,
                    ModelCatalog = null,
                    ModelSelections = [],
                    Cost = null,
                    Workflow = null,
                    SemanticStatus = null,
                    SemanticRebuild = null,
                    SemanticSearch = null,
                    CommitPreview = null,
                    CommitApproval = null,
                    CapabilityApprovals = [],
                    Status = "Goal aborted. Describe a new goal when ready.",
                },
                ComposerText = string.Empty,
                Error = null,
            });
        }, "Goal abort");
    }

    internal void CancelGoalWorkflow() => workflowExecution?.Cancel();

    internal async ValueTask RefreshSemanticStatusAsync(
        GoalId goalId,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            SemanticIndexRequest? request = SemanticRequest(goalId);
            if (request is null)
            {
                PublishGoalStatus("An active workspace is required for semantic context.");
                return;
            }

            SemanticIndexStatusResult result = await semanticIndexService.GetStatusAsync(
                request,
                cancellationToken);
            RemoteCostReport? cost = await remoteCostService.GetAsync(goalId, cancellationToken);
            Publish(Current with
            {
                Goals = Current.Goals with
                {
                    SemanticStatus = result,
                    Cost = cost,
                    Status = result.Error ?? "Semantic status refreshed without inference.",
                },
            });
        }, "Semantic status inspection");

    internal async ValueTask RebuildSemanticIndexAsync(
        GoalId goalId,
        CancellationToken cancellationToken) =>
        await RunSemanticOperationAsync(
            goalId,
            async (request, token) =>
            {
                SemanticIndexResult result = await semanticIndexService.RebuildAsync(request, token);
                SemanticIndexStatusResult status = await semanticIndexService.GetStatusAsync(request, token);
                Publish(Current with
                {
                    Goals = Current.Goals with
                    {
                        SemanticRebuild = result,
                        SemanticStatus = status,
                        Status = result.Error ??
                                 $"Semantic index ready with {result.Partition?.ChunkCount ?? 0} chunks.",
                    },
                });
            },
            cancellationToken,
            "Semantic rebuild");

    internal async ValueTask SearchSemanticContextAsync(
        GoalId goalId,
        string query,
        CancellationToken cancellationToken) =>
        await RunSemanticOperationAsync(
            goalId,
            async (request, token) =>
            {
                SemanticSearchResult result = await semanticIndexService.SearchAsync(new(
                    request.WorkspaceId,
                    query,
                    MaximumResults: 8,
                    request.RemoteGoalId,
                    request.PrivacyPolicy), token);
                Publish(Current with
                {
                    Goals = Current.Goals with
                    {
                        SemanticSearch = result,
                        Status = result.Error ??
                                 $"Semantic preview returned {result.Matches.Count} match(es).",
                    },
                });
            },
            cancellationToken,
            "Semantic search");

    internal void CancelSemanticOperation() => semanticExecution?.Cancel();

    internal async ValueTask RefreshCommitAsync(
        GoalId goalId,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            GoalWorkflowSnapshot? workflow = await goalWorkflowService.GetLatestAsync(
                goalId,
                cancellationToken);
            if (workflow is null)
            {
                PublishGoalStatus("The selected goal has no production run.");
                return;
            }

            GoalCommitApprovalView? approval = await goalAcceptanceService.GetAsync(
                goalId,
                workflow.Id,
                cancellationToken);
            GoalCommitPreviewResult? previewResult = approval is null
                ? await goalAcceptanceService.PreviewAsync(goalId, cancellationToken)
                : null;
            Publish(Current with
            {
                Goals = Current.Goals with
                {
                    Workflow = workflow,
                    CommitPreview = previewResult?.Preview,
                    CommitApproval = approval,
                    Status = approval is not null
                        ? $"Commit approval is {approval.State}."
                        : previewResult?.Error ?? "Exact commit preview loaded.",
                },
            });
        }, "Commit preview");

    internal async ValueTask RequestCommitApprovalAsync(
        GoalCommitMessage message,
        GoalCommitAuthorName authorName,
        GoalCommitAuthorEmail authorEmail,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            GoalCommitPreview? preview = Current.Goals.CommitPreview;
            if (preview is null)
            {
                PublishGoalStatus("Load an exact commit preview before recording a request.");
                return;
            }

            GoalCommitApprovalResult result = await goalAcceptanceService.RequestAsync(new(
                preview.GoalId,
                preview.RunId,
                preview.Head,
                preview.DiffHash,
                message,
                authorName,
                authorEmail), cancellationToken);
            if (result.Approval is null)
            {
                PublishGoalStatus(result.Error ?? "Commit approval request failed.");
                return;
            }

            Publish(Current with
            {
                Goals = Current.Goals with
                {
                    CommitPreview = null,
                    CommitApproval = result.Approval,
                    Status = "Exact commit request recorded as Pending. A separate decision is required.",
                },
            });
        }, "Commit approval request");

    internal async ValueTask DecideCommitAsync(
        GoalCommitDecision decision,
        GoalCommitDecisionReason? reason,
        CancellationToken cancellationToken) =>
        await RunGoalCommandAsync(async () =>
        {
            GoalCommitApprovalView? approval = Current.Goals.CommitApproval;
            if (approval is null)
            {
                PublishGoalStatus("No commit approval request is loaded.");
                return;
            }

            GoalCommitApprovalResult result = await goalAcceptanceService.DecideAsync(new(
                approval.Id,
                decision,
                reason), cancellationToken);
            if (result.Approval is null)
            {
                PublishGoalStatus(result.Error ?? "Commit decision failed.");
                return;
            }

            GoalWorkflowSnapshot? workflow = await goalWorkflowService.GetLatestAsync(
                result.Approval.GoalId,
                cancellationToken);
            string status = result.Approval.State switch
            {
                GoalCommitApprovalState.Denied => "Commit denied; no Git commit was created.",
                GoalCommitApprovalState.Approved =>
                    result.Error ?? "Commit remains approved and can be resumed safely.",
                GoalCommitApprovalState.Committed =>
                    $"Committed exact approved diff: {result.Approval.CommitSha?.Value}",
                GoalCommitApprovalState.Pending => "Commit decision remains pending.",
                _ => throw new InvalidOperationException("Unsupported commit approval state."),
            };
            Publish(Current with
            {
                Goals = Current.Goals with
                {
                    CommitApproval = result.Approval,
                    Workflow = workflow,
                    Status = status,
                },
            });
        }, "Commit decision");

}
