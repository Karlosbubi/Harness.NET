namespace Harness.DataAccess.Agents;

public interface IAgentRoleDefaultStore
{
    ValueTask<IReadOnlyList<StoredAgentRoleDefault>> ListAsync(
        CancellationToken cancellationToken = default);

    ValueTask<StoredAgentRoleDefault> SaveAsync(
        StoredAgentRoleDefault value,
        CancellationToken cancellationToken = default);
}
