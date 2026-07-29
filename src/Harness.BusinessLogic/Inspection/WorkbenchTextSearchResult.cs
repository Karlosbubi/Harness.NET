using Harness.BusinessLogic.Workspaces;

namespace Harness.BusinessLogic.Inspection;

public sealed record WorkbenchTextSearchResult(
    WorkbenchWorkspaceContext Context,
    WorkspaceTextSearchView Search);
