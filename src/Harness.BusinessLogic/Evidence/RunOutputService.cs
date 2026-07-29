using System.Text.Json;
using System.Text.Json.Serialization;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Mutations;

namespace Harness.BusinessLogic.Evidence;

internal sealed class RunOutputService(IToolEvidenceService evidenceService) : IRunOutputService
{
    private const int MaximumRuns = 200;
    private const int MaximumStreamCharacters = 64 * 1024;
    private const int MaximumResultCharacters = 160 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async ValueTask<RunOutputSnapshot> ListAsync(
        GoalId goalId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(goalId);
        if (string.IsNullOrWhiteSpace(goalId.Value))
        {
            return new([], IsTruncated: false, "invalid_goal", "A goal is required to view run output.");
        }

        ToolEvidenceSnapshot evidence = await evidenceService.ListAsync(
            goalId.Value,
            cancellationToken);
        if (evidence.Error is not null)
        {
            return new([], IsTruncated: false, evidence.ErrorCode, evidence.Error);
        }

        ToolEvidenceView[] runs = evidence.Items
            .Where(item => item.Tool is ToolKind.Build or ToolKind.Test or ToolKind.Restore)
            .OrderByDescending(item => item.StartedAt)
            .ToArray();
        return new(
            runs
                .Take(MaximumRuns)
                .Select(item => Map(goalId, item))
                .ToArray(),
            IsTruncated: runs.Length > MaximumRuns,
            ErrorCode: null,
            Error: null);
    }

    private static RunOutputView Map(GoalId goalId, ToolEvidenceView item)
    {
        DotNetOperation operation = item.Tool switch
        {
            ToolKind.Build => DotNetOperation.Build,
            ToolKind.Test => DotNetOperation.Test,
            ToolKind.Restore => DotNetOperation.Restore,
            _ => throw new ArgumentOutOfRangeException(nameof(item)),
        };
        if (item.ResultJson is null)
        {
            return new(
                item.Id,
                goalId,
                item.CorrelationId,
                operation,
                item.State,
                Result: null,
                item.StartedAt,
                item.CompletedAt,
                Error: item.State is ToolEvidenceState.Running or ToolEvidenceState.Uncertain
                    ? null
                    : "The durable run has no result payload.");
        }

        if (item.ResultJson.Length > MaximumResultCharacters)
        {
            return InvalidResult(goalId, item, operation,
                "The durable run result exceeds the supported output bound.");
        }

        try
        {
            DotNetOperationView? result = JsonSerializer.Deserialize<DotNetOperationView>(
                item.ResultJson,
                JsonOptions);
            string? error = result is null ||
                            result.GoalId != goalId.Value ||
                            result.CorrelationId != item.CorrelationId ||
                            result.Operation != operation
                ? "The durable run result does not match its recorded request."
                : result.StandardOutput.Length > MaximumStreamCharacters ||
                  result.StandardError.Length > MaximumStreamCharacters
                    ? "The durable run streams exceed the supported output bound."
                    : null;
            return new(
                item.Id,
                goalId,
                item.CorrelationId,
                operation,
                item.State,
                error is null ? result : null,
                item.StartedAt,
                item.CompletedAt,
                error);
        }
        catch (JsonException)
        {
            return InvalidResult(goalId, item, operation,
                "The durable run result could not be decoded.");
        }
    }

    private static RunOutputView InvalidResult(
        GoalId goalId,
        ToolEvidenceView item,
        DotNetOperation operation,
        string error) => new(
        item.Id,
        goalId,
        item.CorrelationId,
        operation,
        item.State,
        Result: null,
        item.StartedAt,
        item.CompletedAt,
        error);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
