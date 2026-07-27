namespace Harness.DataAccess.Inspection;

public sealed record DotNetSdkPolicy(
    string? Version,
    string? RollForward,
    bool? AllowPrerelease);
