using Harness.BusinessLogic.Workspaces;

namespace Harness.Presentation.Terminal.Tests;

public sealed class WorkspaceDialogTests
{
    [Fact]
    public void Active_workspace_label_exposes_identity_and_status()
    {
        WorkspaceView workspace = new(
            "workspace-1",
            "/work/acme/service",
            "service",
            "/work/acme/service/service.slnx",
            IsTrusted: true,
            IsActive: true,
            "feature/one",
            IsDirty: false);

        string label = WorkspaceDialog.FormatWorkspace(workspace);

        Assert.Contains("[ACTIVE]", label, StringComparison.Ordinal);
        Assert.Contains("service", label, StringComparison.Ordinal);
        Assert.Contains("trusted", label, StringComparison.Ordinal);
        Assert.Contains("feature/one", label, StringComparison.Ordinal);
        Assert.Contains("/work/acme/service", label, StringComparison.Ordinal);
    }

    [Fact]
    public void Inactive_workspace_label_does_not_claim_active_status()
    {
        WorkspaceView workspace = new(
            "workspace-2",
            "/work/acme/library",
            "library",
            "/work/acme/library/library.csproj",
            IsTrusted: false,
            IsActive: false,
            "main",
            IsDirty: true);

        string label = WorkspaceDialog.FormatWorkspace(workspace);

        Assert.DoesNotContain("[ACTIVE]", label, StringComparison.Ordinal);
        Assert.Contains("untrusted", label, StringComparison.Ordinal);
        Assert.Contains("/work/acme/library", label, StringComparison.Ordinal);
    }
}
