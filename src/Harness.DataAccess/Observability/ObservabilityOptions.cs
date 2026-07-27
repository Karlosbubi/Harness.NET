namespace Harness.DataAccess.Observability;

public sealed record ObservabilityOptions(
    string LogDirectory,
    Uri? OtlpEndpoint);
