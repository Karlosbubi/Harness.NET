namespace Harness.DataAccess.Models;

public sealed record ChatRequest(
    string Model,
    IReadOnlyList<ChatMessage> Messages,
    RemoteModelScope? RemoteScope = null,
    MaximumOutputTokens? MaximumOutputTokens = null,
    IReadOnlyList<ChatToolDefinition>? Tools = null);
