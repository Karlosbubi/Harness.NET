namespace Harness.DataAccess.Goals;

public sealed record StoredPlanSnapshot(
    StoredGoal Goal,
    StoredPlan Plan,
    StoredApproval? Approval);
