namespace Harness.DataAccess.Persistence;

public interface IDatabaseInitializer
{
    ValueTask<DatabaseInitializationResult> InitializeAsync(
        CancellationToken cancellationToken = default);
}
