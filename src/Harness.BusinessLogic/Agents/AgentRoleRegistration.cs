using Harness.DataAccess.Models;

namespace Harness.BusinessLogic.Agents;

internal sealed record AgentRoleRegistration(
    AgentRole Role,
    AgentModel Model,
    IModelProvider Provider);
