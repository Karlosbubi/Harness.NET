using System.Text.Json;
using System.Text.Json.Serialization;
using Harness.BusinessLogic.Evidence;
using Harness.BusinessLogic.Goals;
using Harness.BusinessLogic.Mutations;
using Harness.BusinessLogic.Tools;

namespace Harness.BusinessLogic.Tests.Evidence;

public sealed class RunOutputServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task Projects_only_typed_dotnet_runs_in_latest_first_order()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ToolEvidenceView fileEdit = Evidence(
            ToolKind.FileEdit,
            ToolEvidenceState.Succeeded,
            now.AddMinutes(-2),
            "edit-1",
            "{}");
        DotNetOperationView build = Result(
            DotNetOperation.Build,
            "build-1",
            exitCode: 1,
            standardOutput: "bounded stdout",
            standardError: "bounded stderr",
            isOutputTruncated: true);
        DotNetOperationView test = Result(
            DotNetOperation.Test,
            "test-1",
            exitCode: 0,
            standardOutput: "Passed!",
            standardError: string.Empty,
            isOutputTruncated: false);
        RunOutputService service = new(new EvidenceService(new(
            [
                fileEdit,
                Evidence(ToolKind.Build, ToolEvidenceState.Failed, now.AddMinutes(-1),
                    "build-1", JsonSerializer.Serialize(build, JsonOptions)),
                Evidence(ToolKind.Test, ToolEvidenceState.Succeeded, now,
                    "test-1", JsonSerializer.Serialize(test, JsonOptions)),
            ], null, null)));

        RunOutputSnapshot result = await service.ListAsync(new("goal-id"));

        Assert.Null(result.Error);
        Assert.Equal([DotNetOperation.Test, DotNetOperation.Build],
            result.Items.Select(item => item.Operation).ToArray());
        RunOutputView failed = result.Items[1];
        Assert.Equal(ToolEvidenceState.Failed, failed.State);
        Assert.Equal("bounded stdout", failed.Result?.StandardOutput);
        Assert.Equal("bounded stderr", failed.Result?.StandardError);
        Assert.True(failed.Result?.IsOutputTruncated);
        Assert.Null(failed.Error);
    }

    [Fact]
    public async Task Running_and_corrupt_evidence_remain_honest_without_raw_json_leaking()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        RunOutputService service = new(new EvidenceService(new(
            [
                Evidence(ToolKind.Build, ToolEvidenceState.Running, now, "running-1", null),
                Evidence(ToolKind.Test, ToolEvidenceState.Failed, now.AddSeconds(-1),
                    "test-1", "not-json"),
            ], null, null)));

        RunOutputSnapshot result = await service.ListAsync(new("goal-id"));

        Assert.Null(result.Items[0].Result);
        Assert.Null(result.Items[0].Error);
        Assert.Null(result.Items[1].Result);
        Assert.Contains("could not be decoded", result.Items[1].Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preserves_authorization_failure_from_the_evidence_boundary()
    {
        RunOutputService service = new(new EvidenceService(new(
            [], "workspace_not_active", "The goal workspace must be active.")));

        RunOutputSnapshot result = await service.ListAsync(new("goal-id"));

        Assert.Equal("workspace_not_active", result.ErrorCode);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Bounds_the_run_catalog_and_rejects_an_oversized_stream()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<ToolEvidenceView> evidence = Enumerable.Range(0, 201)
            .Select(index => Evidence(
                ToolKind.Build,
                ToolEvidenceState.Running,
                now.AddSeconds(index),
                $"build-{index}",
                resultJson: null))
            .ToList();
        DotNetOperationView oversized = Result(
            DotNetOperation.Test,
            "oversized-test",
            exitCode: 1,
            standardOutput: new string('x', (64 * 1024) + 1),
            standardError: string.Empty,
            isOutputTruncated: false);
        evidence.Add(Evidence(
            ToolKind.Test,
            ToolEvidenceState.Failed,
            now.AddHours(1),
            "oversized-test",
            JsonSerializer.Serialize(oversized, JsonOptions)));
        RunOutputService service = new(new EvidenceService(new(evidence, null, null)));

        RunOutputSnapshot result = await service.ListAsync(new("goal-id"));

        Assert.True(result.IsTruncated);
        Assert.Equal(200, result.Items.Count);
        Assert.Contains("exceed", result.Items[0].Error, StringComparison.Ordinal);
        Assert.Null(result.Items[0].Result);
    }

    private static ToolEvidenceView Evidence(
        ToolKind tool,
        ToolEvidenceState state,
        DateTimeOffset startedAt,
        string correlation,
        string? resultJson) => new(
        new(Guid.NewGuid().ToString("N")),
        "goal-id",
        new(correlation),
        tool,
        "{}",
        state,
        resultJson,
        startedAt,
        state is ToolEvidenceState.Running ? null : startedAt.AddSeconds(1));

    private static DotNetOperationView Result(
        DotNetOperation operation,
        string correlation,
        int exitCode,
        string standardOutput,
        string standardError,
        bool isOutputTruncated) => new(
        "goal-id",
        new(correlation),
        operation,
        "Harness.slnx",
        exitCode,
        standardOutput,
        standardError,
        isOutputTruncated,
        IsErrorTruncated: false,
        WasCancelled: false,
        DurationMilliseconds: 1200,
        ErrorCode: exitCode == 0 ? null : "process_failed",
        Error: exitCode == 0 ? null : "The process failed.");

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class EvidenceService(ToolEvidenceSnapshot result) : IToolEvidenceService
    {
        public ValueTask<ToolEvidenceSnapshot> ListAsync(
            string goalId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(result);
    }
}
