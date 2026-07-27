namespace Harness.DataAccess.Evidence;

public interface IToolEvidenceStore
{
    ValueTask<StoredToolCallStart> StartAsync(
        StoredToolCall toolCall,
        CancellationToken cancellationToken = default);

    ValueTask<StoredToolCall> CompleteAsync(
        ToolCallId toolCallId,
        ToolCallState expectedState,
        ToolCallState nextState,
        string resultJson,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<StoredToolCall>> ListAsync(
        string goalId,
        CancellationToken cancellationToken = default);
}
