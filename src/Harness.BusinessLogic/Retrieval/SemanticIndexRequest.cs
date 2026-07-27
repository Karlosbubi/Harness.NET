namespace Harness.BusinessLogic.Retrieval;

public sealed record SemanticIndexRequest(
    string WorkspaceId,
    string? RemoteGoalId = null,
    SemanticPrivacyPolicy PrivacyPolicy = SemanticPrivacyPolicy.Normal);
