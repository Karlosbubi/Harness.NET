namespace Harness.BusinessLogic.Retrieval;

public sealed record SemanticSearchRequest(
    string WorkspaceId,
    string Query,
    int MaximumResults,
    string? RemoteGoalId = null,
    SemanticPrivacyPolicy PrivacyPolicy = SemanticPrivacyPolicy.Normal);
