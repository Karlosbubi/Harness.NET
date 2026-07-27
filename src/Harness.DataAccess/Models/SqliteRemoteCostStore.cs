using System.Globalization;
using Dapper;
using Harness.DataAccess.Configuration;
using Microsoft.Data.Sqlite;

namespace Harness.DataAccess.Models;

internal sealed class SqliteRemoteCostStore(IApplicationPaths applicationPaths) : IRemoteCostStore
{
    public async ValueTask<RemoteCostLedger?> GetLedgerAsync(
        string goalId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        long? cap = await connection.QuerySingleOrDefaultAsync<long?>(new CommandDefinition("""
            SELECT remote_budget_microusd FROM goals WHERE id = @goalId;
            """, new { goalId }, cancellationToken: cancellationToken));
        if (cap is null)
        {
            return null;
        }

        IEnumerable<CostRow> rows = await connection.QueryAsync<CostRow>(new CommandDefinition("""
            SELECT id, provider, model, operation,
                   estimated_microusd AS EstimatedMicrousd,
                   actual_microusd AS ActualMicrousd,
                   state, created_at AS CreatedAt, completed_at AS CompletedAt
            FROM remote_model_cost_reservations
            WHERE goal_id = @goalId
            ORDER BY created_at, id;
            """, new { goalId }, cancellationToken: cancellationToken));
        RemoteCostEntry[] entries = rows.Select(row => row.ToRecord()).ToArray();
        long reserved = entries
            .Where(entry => entry.State is RemoteCostReservationState.Reserved)
            .Sum(entry => entry.EstimatedCost.Value);
        long reconciled = entries
            .Where(entry => entry.State is RemoteCostReservationState.Reconciled)
            .Sum(entry => entry.ActualCost?.Value ?? 0);
        long balance = cap.Value - reserved - reconciled;
        return new(
            goalId,
            new(cap.Value),
            new(reserved),
            new(reconciled),
            new(Math.Max(0, balance)),
            new(Math.Max(0, -balance)),
            entries);
    }

    public async ValueTask<RemoteCostReservationResult> ReserveAsync(
        RemoteCostReservationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(request.EstimatedCost.Value);

        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        string reservationId = Guid.NewGuid().ToString("N");
        int inserted = await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO remote_model_cost_reservations (
                id, goal_id, provider, model, operation, estimated_microusd,
                actual_microusd, state, created_at, completed_at)
            SELECT @reservationId, goals.id, @provider, @model, @operation,
                   @estimatedCost, NULL, 'Reserved', @createdAt, NULL
            FROM goals
            WHERE goals.id = @goalId
              AND goals.state = 'Approved'
              AND goals.remote_budget_microusd IS NOT NULL
              AND (
                  SELECT COALESCE(SUM(
                      CASE state
                          WHEN 'Reserved' THEN estimated_microusd
                          WHEN 'Reconciled' THEN actual_microusd
                          ELSE 0
                      END), 0)
                  FROM remote_model_cost_reservations
                  WHERE goal_id = goals.id
              ) + @estimatedCost <= goals.remote_budget_microusd;
            """, new
        {
            reservationId,
            goalId = request.GoalId,
            provider = request.Provider,
            model = request.Model,
            operation = request.Operation.ToString(),
            estimatedCost = request.EstimatedCost.Value,
            createdAt = Format(DateTimeOffset.UtcNow),
        }, cancellationToken: cancellationToken));

        if (inserted == 1)
        {
            return new(new(reservationId, request.EstimatedCost), Failure: null);
        }

        bool authorized = await connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            SELECT COUNT(*) FROM goals
            WHERE id = @goalId
              AND state = 'Approved'
              AND remote_budget_microusd IS NOT NULL;
            """, new { goalId = request.GoalId }, cancellationToken: cancellationToken)) == 1;
        return authorized
            ? new(null, RemoteCostReservationFailure.CostCapExceeded)
            : new(null, RemoteCostReservationFailure.GoalNotApprovedOrAuthorized);
    }

    public ValueTask ReconcileAsync(
        string reservationId,
        MicroUsd actualCost,
        CancellationToken cancellationToken = default) =>
        CompleteAsync(reservationId, "Reconciled", actualCost.Value, cancellationToken);

    public ValueTask ReleaseAsync(
        string reservationId,
        CancellationToken cancellationToken = default) =>
        CompleteAsync(reservationId, "Released", actualCost: null, cancellationToken);

    private async ValueTask CompleteAsync(
        string reservationId,
        string state,
        long? actualCost,
        CancellationToken cancellationToken)
    {
        if (actualCost is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actualCost));
        }

        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        int updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE remote_model_cost_reservations
            SET state = @state, actual_microusd = @actualCost, completed_at = @completedAt
            WHERE id = @reservationId AND state = 'Reserved';
            """, new
        {
            reservationId,
            state,
            actualCost,
            completedAt = Format(DateTimeOffset.UtcNow),
        }, cancellationToken: cancellationToken));
        if (updated != 1)
        {
            throw new InvalidOperationException("The remote-cost reservation is not active.");
        }
    }

    private async ValueTask<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(new SqliteConnectionStringBuilder
        {
            DataSource = applicationPaths.Current.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static string Format(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private sealed class CostRow
    {
        public string Id { get; init; } = string.Empty;

        public string Provider { get; init; } = string.Empty;

        public string Model { get; init; } = string.Empty;

        public string Operation { get; init; } = string.Empty;

        public long EstimatedMicrousd { get; init; }

        public long? ActualMicrousd { get; init; }

        public string State { get; init; } = string.Empty;

        public string CreatedAt { get; init; } = string.Empty;

        public string? CompletedAt { get; init; }

        public RemoteCostEntry ToRecord() => new(
            Id,
            Provider,
            Model,
            Enum.Parse<RemoteCostOperation>(Operation),
            new(EstimatedMicrousd),
            ActualMicrousd is null ? null : new(ActualMicrousd.Value),
            Enum.Parse<RemoteCostReservationState>(State),
            DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture),
            CompletedAt is null
                ? null
                : DateTimeOffset.Parse(CompletedAt, CultureInfo.InvariantCulture));
    }
}
