namespace Harness.BusinessLogic.Agents;

public sealed record AgentToolModuleId(string Value);

public sealed record AgentToolSourceName(string Value);

public sealed record AgentToolOperationName(string Value);

public enum AgentToolModuleAvailability
{
    Available,
    Planned,
}

public enum AgentToolExposure
{
    Direct,
    OnDemand,
}

public enum AgentToolAuthority
{
    TrustedRead,
    ApprovedWorktreeMutation,
    RepositoryExecution,
    ExternalOrSensitive,
}

public sealed record AgentToolModule(
    AgentToolModuleId Id,
    string DisplayName,
    string Summary,
    AgentToolSourceName Source,
    AgentToolModuleAvailability Availability,
    IReadOnlyList<AgentRole> Roles,
    AgentToolExposure Exposure,
    AgentToolAuthority Authority,
    IReadOnlyList<AgentToolOperationName> Operations,
    bool IsOptional,
    string? UnavailableReason);

public sealed record AgentToolCatalog(IReadOnlyList<AgentToolModule> Modules)
{
    public static AgentToolCatalog Default { get; } = new(
    [
        new(
            new("workspace-inspection"),
            "Workspace inspection",
            "Bounded files, text, Git state, and evaluated .NET project metadata.",
            new("Harness.NET"),
            AgentToolModuleAvailability.Available,
            [AgentRole.Lead, AgentRole.Implementer, AgentRole.Reviewer],
            AgentToolExposure.Direct,
            AgentToolAuthority.TrustedRead,
            [new("read_file"), new("read_file_range"), new("list_workspace_tree"),
                new("search_text"), new("search_regex"), new("inspect_git"),
                new("inspect_dotnet"), new("inspect_project_graph"),
                new("inspect_open_documents")],
            IsOptional: false,
            UnavailableReason: null),
        new(
            new("roslyn-semantic-analysis"),
            "Roslyn semantic analysis",
            "Current compiler diagnostics, symbol information, definitions, references, and implementations from the exact role source context.",
            new("Harness.NET · Roslyn"),
            AgentToolModuleAvailability.Available,
            [AgentRole.Lead, AgentRole.Implementer, AgentRole.Reviewer],
            AgentToolExposure.Direct,
            AgentToolAuthority.TrustedRead,
            [new("inspect_code_problems"), new("inspect_project_problems"),
                new("get_symbol_info"),
                new("find_symbol_definition"), new("find_symbol_references"),
                new("find_symbol_implementations")],
            IsOptional: false,
            UnavailableReason: null),
        new(
            new("roslyn-transformations"),
            "Roslyn deterministic transformations",
            "Previewed and fingerprinted semantic changes with exact-baseline apply.",
            new("Harness.NET · Roslyn"),
            AgentToolModuleAvailability.Available,
            [AgentRole.Implementer],
            AgentToolExposure.Direct,
            AgentToolAuthority.ApprovedWorktreeMutation,
            [new("preview_symbol_rename"), new("apply_symbol_rename"),
                new("find_missing_imports"),
                new("preview_document_transformation"),
                new("apply_document_transformation")],
            IsOptional: false,
            UnavailableReason: null),
        new(
            new("visual-verification"),
            "Visual verification",
            "One user-approved portal frame, stored as bounded goal evidence and inspected only under the configured local or remote disclosure policy.",
            new("Harness.NET · XDG Desktop Portal"),
            AgentToolModuleAvailability.Available,
            [AgentRole.Lead, AgentRole.Implementer, AgentRole.Reviewer],
            AgentToolExposure.Direct,
            AgentToolAuthority.ExternalOrSensitive,
            [new("request_visual_capture"), new("inspect_visual_capture")],
            IsOptional: true,
            UnavailableReason: null),
        new(
            new("documentation-research"),
            "Versioned documentation research",
            "Bounded cited lookup through exact local, indexed, configured MCP, and web sources in authority order.",
            new("Harness.NET · configured documentation sources"),
            AgentToolModuleAvailability.Available,
            [AgentRole.Lead, AgentRole.Implementer, AgentRole.Reviewer],
            AgentToolExposure.OnDemand,
            AgentToolAuthority.ExternalOrSensitive,
            [new("lookup_documentation")],
            IsOptional: true,
            UnavailableReason: null),
        new(
            new("dependency-evidence"),
            "Dependency evidence and SBOM",
            "Declared, central, direct, transitive, and restored package evidence with deterministic SBOM preview.",
            new("Harness.NET · NuGet metadata"),
            AgentToolModuleAvailability.Available,
            [AgentRole.Lead, AgentRole.Implementer, AgentRole.Reviewer],
            AgentToolExposure.OnDemand,
            AgentToolAuthority.TrustedRead,
            [new("inspect_dependencies"), new("validate_package_candidate"),
                new("preview_sbom"), new("preview_package_change")],
            IsOptional: true,
            UnavailableReason: null),
        new(
            new("semantic-hierarchy"),
            "Roslyn hierarchy and test discovery",
            "Symbol search, calls, type hierarchy, override hierarchy, and associated tests.",
            new("Harness.NET · Roslyn"),
            AgentToolModuleAvailability.Available,
            [AgentRole.Lead, AgentRole.Implementer, AgentRole.Reviewer],
            AgentToolExposure.OnDemand,
            AgentToolAuthority.TrustedRead,
            [new("search_symbols"), new("analyze_calls"), new("get_type_hierarchy"),
                new("find_associated_tests"), new("post_edit_quality_check")],
            IsOptional: true,
            UnavailableReason: null),
        new(
            new("build-test"),
            "Build and test",
            "Typed build and test execution without an implicit restore.",
            new("Harness.NET · .NET SDK"),
            AgentToolModuleAvailability.Available,
            [AgentRole.Implementer],
            AgentToolExposure.Direct,
            AgentToolAuthority.RepositoryExecution,
            [new("dotnet_build"), new("dotnet_test")],
            IsOptional: false,
            UnavailableReason: null),
    ]);
}
