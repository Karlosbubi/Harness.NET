using System.Security.Cryptography;
using System.Text;
using Harness.BusinessLogic.Acceptance;
using Harness.BusinessLogic.Approvals;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Tools;
using Harness.BusinessLogic.Workspaces;
using Harness.DataAccess.Approvals;
using Harness.DataAccess.Commits;
using Harness.DataAccess.Configuration;
using Harness.DataAccess.Evidence;
using Harness.DataAccess.Execution;
using Harness.DataAccess.Goals;
using Harness.DataAccess.Mutations;
using Harness.DataAccess.Persistence;
using Harness.DataAccess.Workflows;
using Harness.DataAccess.Workspaces;
using Harness.DataAccess.Worktrees;
using LibGit2Sharp;

namespace Harness.BusinessLogic.Tests.Acceptance;

public sealed class RepresentativeRepositoryAcceptanceTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "harness-release-acceptance", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Trusted_repository_can_be_edited_verified_reviewed_and_committed_in_isolation()
    {
        ApplicationPaths paths = CreatePaths();
        StubApplicationPaths applicationPaths = new(paths);
        await new SqliteDatabaseInitializer(applicationPaths).InitializeAsync();
        string repositoryRoot = await CreateRepositoryAsync();
        string entryPoint = Path.Combine(repositoryRoot, "Representative.csproj");

        SqliteWorkspaceStore workspaceStore = new(applicationPaths);
        WorkspaceService workspaces = new(new GitWorkspaceInspector(), workspaceStore);
        WorkspaceResult registered = await workspaces.RegisterAsync(repositoryRoot, entryPoint);
        Assert.Null(registered.Error);
        WorkspaceResult trusted = await workspaces.SetTrustAsync(registered.Workspace!.Id, true);
        Assert.True(trusted.Workspace?.IsTrusted);

        SqliteGoalStore goalStore = new(applicationPaths);
        GoalService goals = new(goalStore, workspaceStore, new GitGoalWorktreeManager(applicationPaths));
        GoalResult created = await goals.CreateAsync(new(
            registered.Workspace.Id,
            "Update representative greeting",
            "Change the greeting and verify the isolated project.",
            new(2),
            RemoteBudget: null));
        PlanResult proposed = await goals.ProposePlanAsync(new(
            created.Goal!.Id,
            "Edit Program.cs, restore explicitly, build, test, review, and request the exact commit."));
        PlanResult approved = await goals.DecidePlanAsync(new(
            created.Goal.Id,
            proposed.Plan!.Id,
            PlanDecision.Approve,
            "Proceed with the bounded local workflow."));
        Assert.Equal(GoalState.Approved, approved.Goal?.State);
        string worktreePath = approved.Worktree!.Path;

        SqliteCapabilityApprovalStore approvalStore = new(applicationPaths);
        CapabilityApprovalService approvals = new(goalStore, workspaceStore, approvalStore);
        SqliteToolEvidenceStore evidenceStore = new(applicationPaths);
        WorkspaceMutationService mutations = new(
            goalStore,
            workspaceStore,
            new AtomicWorkspaceFileEditor(),
            new DotNetToolRunner(),
            evidenceStore,
            approvalStore);

        const string updatedProgram = "Console.WriteLine(\"verified by Harness.NET\");\n";
        FileEditView edit = await mutations.ApplyFileEditAsync(new(
            created.Goal.Id.Value,
            new("representative-edit"),
            "Program.cs",
            Hash("Console.WriteLine(\"before\");\n"),
            updatedProgram));
        Assert.Null(edit.Error);

        ToolCorrelationId restoreCorrelation = new("representative-restore");
        CapabilityApprovalResult restoreRequest = await approvals.RequestAsync(new(
            created.Goal.Id.Value,
            restoreCorrelation,
            Harness.BusinessLogic.Approvals.CapabilityKind.Restore,
            "Generate local assets for the representative project."));
        CapabilityApprovalResult restoreApproval = await approvals.DecideAsync(new(
            restoreRequest.Approval!.Id,
            CapabilityDecision.Approve,
            "Approved for this one local restore."));
        Assert.Equal(Harness.BusinessLogic.Approvals.CapabilityApprovalState.Approved,
            restoreApproval.Approval?.State);

        DotNetOperationView restore = await mutations.RunDotNetAsync(new(
            created.Goal.Id.Value, restoreCorrelation, DotNetOperation.Restore));
        DotNetOperationView build = await mutations.RunDotNetAsync(new(
            created.Goal.Id.Value, new("representative-build"), DotNetOperation.Build));
        DotNetOperationView test = await mutations.RunDotNetAsync(new(
            created.Goal.Id.Value, new("representative-test"), DotNetOperation.Test));
        Assert.Equal(0, restore.ExitCode);
        Assert.Equal(0, build.ExitCode);
        Assert.Equal(0, test.ExitCode);

        SqliteGoalWorkflowStore workflowStore = new(applicationPaths);
        GoalWorkflowRunId runId = new(Guid.NewGuid().ToString("N"));
        DateTimeOffset now = DateTimeOffset.Parse("2026-07-28T00:00:00Z");
        await workflowStore.StartAsync(
            new(runId, new(created.Goal.Id.Value), GoalWorkflowRunState.Running, new(0), now, now),
            Checkpoint(runId, 1, GoalWorkflowCheckpointKind.Started, now));
        await workflowStore.AppendAsync(
            Checkpoint(runId, 0, GoalWorkflowCheckpointKind.LeadCallStarted, now.AddSeconds(1)),
            GoalWorkflowCheckpointKind.Started,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.Running);
        await workflowStore.AppendAsync(
            Checkpoint(runId, 0, GoalWorkflowCheckpointKind.PlanProposed, now.AddSeconds(2)),
            GoalWorkflowCheckpointKind.LeadCallStarted,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.AwaitingPlanApproval);
        await workflowStore.AppendAsync(
            Checkpoint(runId, 0, GoalWorkflowCheckpointKind.PlanApproved, now.AddSeconds(3)),
            GoalWorkflowCheckpointKind.PlanProposed,
            GoalWorkflowRunState.AwaitingPlanApproval,
            GoalWorkflowRunState.Running);
        await workflowStore.AppendAsync(
            Checkpoint(runId, 0, GoalWorkflowCheckpointKind.ImplementerCallStarted,
                now.AddSeconds(4)),
            GoalWorkflowCheckpointKind.PlanApproved,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.Running);
        await workflowStore.AppendAsync(
            Checkpoint(runId, 0, GoalWorkflowCheckpointKind.ImplementationProduced,
                now.AddSeconds(5)),
            GoalWorkflowCheckpointKind.ImplementerCallStarted,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.Running);
        await workflowStore.AppendAsync(
            Checkpoint(runId, 0, GoalWorkflowCheckpointKind.ReviewerCallStarted,
                now.AddSeconds(6)),
            GoalWorkflowCheckpointKind.ImplementationProduced,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.Running);
        await workflowStore.AppendAsync(
            Checkpoint(runId, 0, GoalWorkflowCheckpointKind.ReviewCompleted, now.AddSeconds(7)),
            GoalWorkflowCheckpointKind.ReviewerCallStarted,
            GoalWorkflowRunState.Running,
            GoalWorkflowRunState.AwaitingAcceptance,
            nextReviewCycle: new(1));

        GoalAcceptanceService acceptance = new(
            goalStore,
            workspaceStore,
            workflowStore,
            new SqliteGoalCommitApprovalStore(applicationPaths),
            new LibGitGoalCommitter(),
            new FixedTimeProvider(now.AddSeconds(8)));
        GoalCommitPreview preview = Assert.IsType<GoalCommitPreview>(
            (await acceptance.PreviewAsync(created.Goal.Id)).Preview);
        GoalCommitApprovalResult requested = await acceptance.RequestAsync(new(
            created.Goal.Id,
            preview.RunId,
            preview.Head,
            preview.DiffHash,
            new("Update representative greeting"),
            new("Harness Release Test"),
            new("release@harness.local")));
        GoalCommitApprovalResult committed = await acceptance.DecideAsync(new(
            requested.Approval!.Id,
            GoalCommitDecision.Approve,
            Reason: null));

        Assert.Equal(Harness.BusinessLogic.Acceptance.GoalCommitApprovalState.Committed,
            committed.Approval?.State);
        Assert.Equal("Console.WriteLine(\"before\");\n",
            await File.ReadAllTextAsync(Path.Combine(repositoryRoot, "Program.cs")));
        using Repository original = new(repositoryRoot);
        using Repository worktree = new(worktreePath);
        Assert.Equal("main", original.Head.FriendlyName);
        Assert.Single(original.Commits);
        Assert.Equal(2, worktree.Commits.Count());
        Assert.Contains("Harness-Diff-SHA256:", worktree.Head.Tip.Message,
            StringComparison.Ordinal);
    }

    private async ValueTask<string> CreateRepositoryAsync()
    {
        string repositoryRoot = Path.Combine(root, "repository");
        Directory.CreateDirectory(repositoryRoot);
        Repository.Init(repositoryRoot);
        await File.WriteAllTextAsync(Path.Combine(repositoryRoot, "Representative.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(repositoryRoot, "Program.cs"),
            "Console.WriteLine(\"before\");\n");
        using Repository repository = new(repositoryRoot);
        Commands.Stage(repository, "*");
        Signature signature = new("Harness Tests", "tests@harness.local", DateTimeOffset.UtcNow);
        repository.Commit("initial", signature, signature);
        repository.CreateBranch("main");
        Commands.Checkout(repository, "main");
        return repositoryRoot;
    }

    private ApplicationPaths CreatePaths() => new(
        Path.Combine(root, "config"),
        Path.Combine(root, "data"),
        Path.Combine(root, "state"),
        Path.Combine(root, "cache"),
        Path.Combine(root, "data", "harness.db"),
        Path.Combine(root, "state", "logs"),
        Path.Combine(root, "state", "worktrees"));

    private static string Hash(string content) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private static StoredGoalWorkflowCheckpoint Checkpoint(
        GoalWorkflowRunId runId,
        int sequence,
        GoalWorkflowCheckpointKind kind,
        DateTimeOffset createdAt) => new(
        Guid.NewGuid().ToString("N"),
        runId,
        sequence,
        kind,
        WorkflowActor.System,
        new(kind.ToString()),
        new("Release acceptance"),
        new("Representative repository verification passed."),
        createdAt);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubApplicationPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
