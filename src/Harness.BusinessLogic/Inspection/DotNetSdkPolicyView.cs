namespace Harness.BusinessLogic.Inspection;

public sealed record DotNetSdkPolicyView(
    string? Version,
    string? RollForward,
    bool? AllowPrerelease);
