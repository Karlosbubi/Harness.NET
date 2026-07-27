namespace Harness.DataAccess.Evidence;

public sealed record StoredToolCallStart(
    StoredToolCall ToolCall,
    bool WasCreated);
