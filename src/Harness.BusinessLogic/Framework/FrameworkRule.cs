namespace Harness.BusinessLogic.Framework;

public sealed record FrameworkRule(
    string Key,
    string Value,
    int Precedence,
    string Layer,
    bool IsLocked,
    string Source);
