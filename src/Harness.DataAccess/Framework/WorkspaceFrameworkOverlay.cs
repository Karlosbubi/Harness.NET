namespace Harness.DataAccess.Framework;

public sealed record WorkspaceFrameworkOverlay(
    string WorkspaceId,
    string Content,
    DateTimeOffset UpdatedAt);
