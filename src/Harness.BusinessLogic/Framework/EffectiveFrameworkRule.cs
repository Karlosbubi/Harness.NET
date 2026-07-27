namespace Harness.BusinessLogic.Framework;

public sealed record EffectiveFrameworkRule(
    string Key,
    string Value,
    string Layer,
    bool IsLocked,
    string Source);
