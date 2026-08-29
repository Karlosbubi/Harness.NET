using Harness.BusinessLogic.Workspaces;

namespace Harness.BusinessLogic.Inspection;

public sealed record WorkbenchDotNetInspectionResult(
    WorkbenchWorkspaceContext Context,
    WorkspaceDotNetInfoView DotNet);
