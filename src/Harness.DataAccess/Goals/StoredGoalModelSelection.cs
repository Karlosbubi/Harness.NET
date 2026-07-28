namespace Harness.DataAccess.Goals;

public sealed record StoredGoalModelSelection(
    string GoalId,
    string Role,
    string Provider,
    string Model,
    DateTimeOffset SelectedAt);
