using Harness.DataAccess.Configuration;
using Harness.DataAccess.Models;
using Harness.DataAccess.Persistence;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Tests.Models;

public sealed class SqliteRemoteCostStoreTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        $"harness-cost-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task Reserves_reconciles_and_enforces_aggregate_goal_cap()
    {
        (SqliteRemoteCostStore store, string databasePath) = await CreateStoreAsync(
            goalState: "Approved",
            budgetMicrousd: 100);

        RemoteCostReservationResult first = await store.ReserveAsync(Request(80));
        Assert.NotNull(first.Reservation);
        await store.ReconcileAsync(first.Reservation.Id, new(60));

        RemoteCostReservationResult second = await store.ReserveAsync(Request(40));
        Assert.NotNull(second.Reservation);
        RemoteCostReservationResult overCap = await store.ReserveAsync(Request(1));

        Assert.Equal(RemoteCostReservationFailure.CostCapExceeded, overCap.Failure);
        RemoteCostLedger ledger = Assert.IsType<RemoteCostLedger>(
            await store.GetLedgerAsync("goal-1"));
        Assert.Equal(new MicroUsd(100), ledger.CostCap);
        Assert.Equal(new MicroUsd(40), ledger.ReservedCost);
        Assert.Equal(new MicroUsd(60), ledger.ReconciledCost);
        Assert.Equal(new MicroUsd(0), ledger.RemainingCost);
        Assert.Equal(2, ledger.Entries.Count);
        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT actual_microusd FROM remote_model_cost_reservations
            WHERE id = $id AND state = 'Reconciled';
            """;
        command.Parameters.AddWithValue("$id", first.Reservation.Id);
        Assert.Equal(60L, (long)command.ExecuteScalar()!);
    }

    [Theory]
    [InlineData("Draft", 100L)]
    [InlineData("Approved", null)]
    public async Task Rejects_goals_without_approved_remote_budget(
        string goalState,
        long? budgetMicrousd)
    {
        (SqliteRemoteCostStore store, _) = await CreateStoreAsync(goalState, budgetMicrousd);

        RemoteCostReservationResult result = await store.ReserveAsync(Request(1));

        Assert.Equal(RemoteCostReservationFailure.GoalNotApprovedOrAuthorized, result.Failure);
    }

    [Fact]
    public async Task Explicit_role_selection_authorizes_only_its_remote_model_before_plan_approval()
    {
        (SqliteRemoteCostStore store, string databasePath) = await CreateStoreAsync(
            goalState: "Draft",
            budgetMicrousd: 100);
        using (SqliteConnection connection = new($"Data Source={databasePath}"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO goal_model_selections (goal_id, role, provider, model, selected_at)
                VALUES ('goal-1', 'Lead', 'OpenRouter', 'model', $now);
                """;
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }

        RemoteCostReservationResult authorized = await store.ReserveAsync(Request(1));
        RemoteCostReservationResult wrongModel = await store.ReserveAsync(
            Request(1) with { Model = "another-model" });
        RemoteCostReservationResult wrongRole = await store.ReserveAsync(
            Request(1) with { Role = RemoteModelRole.Reviewer });

        Assert.NotNull(authorized.Reservation);
        Assert.Equal(
            RemoteCostReservationFailure.GoalNotApprovedOrAuthorized,
            wrongModel.Failure);
        Assert.Equal(
            RemoteCostReservationFailure.GoalNotApprovedOrAuthorized,
            wrongRole.Failure);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private async Task<(SqliteRemoteCostStore Store, string DatabasePath)> CreateStoreAsync(
        string goalState,
        long? budgetMicrousd)
    {
        string databasePath = Path.Combine(root, "data", "harness.db");
        ApplicationPaths paths = new(
            Path.Combine(root, "config"),
            Path.Combine(root, "data"),
            Path.Combine(root, "state"),
            Path.Combine(root, "cache"),
            databasePath,
            Path.Combine(root, "state", "logs"),
            Path.Combine(root, "state", "worktrees"));
        StubApplicationPaths applicationPaths = new(paths);
        await new SqliteDatabaseInitializer(applicationPaths).InitializeAsync();

        using SqliteConnection connection = new($"Data Source={databasePath}");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO workspaces (
                id, root_path, name, entry_point, is_trusted, branch, is_dirty,
                created_at, updated_at, is_active)
            VALUES (
                'workspace-1', '/tmp/example', 'Example', '/tmp/example/Example.slnx',
                1, 'main', 0, $now, $now, 1);
            INSERT INTO goals (
                id, workspace_id, title, objective, review_cycle_limit,
                remote_budget_microusd, state, created_at, updated_at)
            VALUES ('goal-1', 'workspace-1', 'Goal', 'Objective', 2,
                    $budget, $state, $now, $now);
            """;
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$budget", (object?)budgetMicrousd ?? DBNull.Value);
        command.Parameters.AddWithValue("$state", goalState);
        command.ExecuteNonQuery();
        return (new(applicationPaths), databasePath);
    }

    private static RemoteCostReservationRequest Request(long estimatedMicrousd) =>
        new(
            "goal-1",
            "OpenRouter",
            "model",
            RemoteCostOperation.Chat,
            new(estimatedMicrousd),
            RemoteModelRole.Lead);

    private sealed class StubApplicationPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }
}
