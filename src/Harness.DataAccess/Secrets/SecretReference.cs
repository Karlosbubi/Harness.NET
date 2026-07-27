namespace Harness.DataAccess.Secrets;

public sealed record SecretReference(string Name, string? EnvironmentVariable = null);
