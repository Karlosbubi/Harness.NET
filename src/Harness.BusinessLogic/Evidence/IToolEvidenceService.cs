namespace Harness.BusinessLogic.Evidence;

public interface IToolEvidenceService
{
    ValueTask<ToolEvidenceSnapshot> ListAsync(
        string goalId,
        CancellationToken cancellationToken = default);
}
