using Harness.BusinessLogic.Workspaces;

namespace Harness.BusinessLogic.Inspection;

public sealed record WorkbenchFileCatalogResult(
    WorkbenchWorkspaceContext Context,
    WorkspaceFileCatalogView Catalog);
