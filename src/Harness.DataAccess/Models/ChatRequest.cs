namespace Harness.DataAccess.Models;

public sealed record ChatRequest(
    string Model,
    IReadOnlyList<ChatMessage> Messages,
    RemoteModelScope? RemoteScope = null,
    IReadOnlyList<ChatToolDefinition>? Tools = null);
