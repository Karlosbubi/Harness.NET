namespace Harness.DataAccess.Models;

public sealed record ChatRequest(
    string Model,
    IReadOnlyList<ChatMessage> Messages);
