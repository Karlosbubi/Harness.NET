using Harness.DataAccess.Mcp;

namespace Harness.BusinessLogic.Mcp;

public enum InboundControlMode { Normal, IsolatedEvaluation }
public sealed record InboundControlClientId(string Value);
public sealed record InboundControlToolId(string Value);
public sealed record InboundControlSettings(
    bool IsEnabled,
    InboundControlMode Mode,
    Uri Endpoint,
    IReadOnlyList<InboundControlClientId> AllowedClients,
    IReadOnlyList<InboundControlToolId> AllowedTools,
    IReadOnlyList<InboundControlToolId> ApprovalRequiredTools,
    TimeSpan RequestTimeout,
    int ResultLimit,
    int AuditRetention,
    bool RequiresRestart);
public sealed record InboundControlClientStatus(
    InboundControlClientId Id, DateTimeOffset LastSeenAt, int RequestCount);
public sealed record InboundControlStatus(
    string InstanceId, bool IsRunning, Uri Endpoint, InboundControlMode Mode,
    IReadOnlyList<InboundControlClientStatus> ActiveClients,
    string? ErrorCode, string? Error);
public sealed record InboundControlToolPolicy(
    InboundControlToolId Id,
    bool IsReadOnly,
    bool IsMutation,
    bool IsExecution,
    bool IsSensitive,
    bool IsDestructive,
    bool IsIdempotent);
public sealed record InboundMcpSettingsView(
    InboundControlSettings Settings,
    InboundControlStatus Status,
    IReadOnlyList<InboundControlToolPolicy> ToolPolicies);
public sealed record InboundControlEvaluationReset(
    InboundMcpSettingsView Settings,
    string Head,
    int ChangedFiles);

public interface IInboundMcpSettingsService
{
    ValueTask<InboundMcpSettingsView> GetAsync(CancellationToken cancellationToken = default);
    ValueTask<InboundMcpSettingsView> SaveAsync(
        InboundControlSettings settings, CancellationToken cancellationToken = default);
    ValueTask<InboundMcpSettingsView> DisconnectAsync(
        InboundControlClientId clientId, CancellationToken cancellationToken = default);
    ValueTask<InboundControlEvaluationReset> ResetEvaluationAsync(
        CancellationToken cancellationToken = default);
}

internal sealed class InboundMcpSettingsService(
    IInboundMcpSettingsStore settingsStore,
    IInboundMcpRuntime runtime,
    IInboundMcpEvaluationFixture evaluationFixture,
    InboundMcpApplicationEnvironment environment,
    IInboundMcpApplication application) : IInboundMcpSettingsService
{
    public async ValueTask<InboundMcpSettingsView> GetAsync(
        CancellationToken cancellationToken = default) =>
        View(await settingsStore.GetAsync(cancellationToken), runtime.Current);

    public async ValueTask<InboundMcpSettingsView> SaveAsync(
        InboundControlSettings settings, CancellationToken cancellationToken = default)
    {
        await settingsStore.SaveAsync(new(
            settings.IsEnabled,
            settings.Mode is InboundControlMode.IsolatedEvaluation
                ? InboundMcpMode.IsolatedEvaluation : InboundMcpMode.Normal,
            settings.Endpoint,
            settings.AllowedClients.Select(item => new InboundMcpClientId(item.Value)).ToArray(),
            settings.AllowedTools.Select(item => new InboundMcpToolId(item.Value)).ToArray(),
            settings.ApprovalRequiredTools.Select(item => new InboundMcpToolId(item.Value)).ToArray(),
            new(settings.RequestTimeout), new(settings.ResultLimit), new(settings.AuditRetention),
            settings.RequiresRestart), cancellationToken);
        await runtime.ApplyAsync(cancellationToken);
        InboundMcpServerSettings current = await settingsStore.GetAsync(cancellationToken);
        return View(current with { RequiresRestart = false }, runtime.Current);
    }

    public async ValueTask<InboundMcpSettingsView> DisconnectAsync(
        InboundControlClientId clientId, CancellationToken cancellationToken = default)
    {
        await runtime.DisconnectAsync(new(clientId.Value), cancellationToken);
        return await GetAsync(cancellationToken);
    }

    public async ValueTask<InboundControlEvaluationReset> ResetEvaluationAsync(
        CancellationToken cancellationToken = default)
    {
        if (!environment.IsIsolatedEvaluation)
            throw new InvalidOperationException(
                "Start Harness with --mcp-evaluation-root before resetting evaluation state.");
        InboundMcpEvaluationSnapshot snapshot = await evaluationFixture.ResetAsync(cancellationToken);
        return new(await GetAsync(cancellationToken), snapshot.Head, snapshot.ChangedFiles);
    }

    private InboundMcpSettingsView View(
        InboundMcpServerSettings settings, InboundMcpServerStatus status) => new(
        new(settings.IsEnabled,
            settings.Mode is InboundMcpMode.IsolatedEvaluation
                ? InboundControlMode.IsolatedEvaluation : InboundControlMode.Normal,
            settings.Endpoint,
            settings.AllowedClients.Select(item => new InboundControlClientId(item.Value)).ToArray(),
            settings.AllowedTools.Select(item => new InboundControlToolId(item.Value)).ToArray(),
            settings.ApprovalRequiredTools.Select(item => new InboundControlToolId(item.Value)).ToArray(),
            settings.RequestTimeout.Value, settings.ResultLimit.Value,
            settings.AuditRetention.Value, settings.RequiresRestart),
        new(status.InstanceId.Value, status.IsRunning, status.Endpoint,
            status.Mode is InboundMcpMode.IsolatedEvaluation
                ? InboundControlMode.IsolatedEvaluation : InboundControlMode.Normal,
            status.ActiveClients.Select(item => new InboundControlClientStatus(
                new(item.Id.Value), item.LastSeenAt, item.RequestCount)).ToArray(),
            status.ErrorCode, status.Error),
        application.ToolPolicies.Select(item => new InboundControlToolPolicy(
            new(item.Id.Value), item.IsReadOnly, item.IsMutation, item.IsExecution,
            item.IsSensitive, item.IsDestructive, item.IsIdempotent)).ToArray());
}
