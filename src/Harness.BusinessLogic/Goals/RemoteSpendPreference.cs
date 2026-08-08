using Harness.BusinessLogic.Costs;

namespace Harness.BusinessLogic.Goals;

public sealed record RemoteSpendPreference(
    RemoteSpendMode Mode,
    MicroUsdAmount? Cap)
{
    public static RemoteSpendPreference Default { get; } = new(
        RemoteSpendMode.Unlimited,
        Cap: null);

    public MicroUsdAmount? ToGoalBudget() => Mode switch
    {
        RemoteSpendMode.Unlimited => new(long.MaxValue),
        RemoteSpendMode.Capped => Cap,
        RemoteSpendMode.LocalOnly => null,
        _ => throw new ArgumentOutOfRangeException(nameof(Mode)),
    };

    public static RemoteSpendPreference FromGoalBudget(MicroUsdAmount? budget) => budget switch
    {
        null => new(RemoteSpendMode.LocalOnly, Cap: null),
        { Value: long.MaxValue } => Default,
        _ => new(RemoteSpendMode.Capped, budget),
    };
}
