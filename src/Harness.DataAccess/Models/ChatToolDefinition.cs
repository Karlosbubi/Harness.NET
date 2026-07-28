namespace Harness.DataAccess.Models;

public sealed record ChatToolDefinition(
    ChatToolName Name,
    ChatToolDescription Description,
    ChatToolJsonSchema JsonSchema);
