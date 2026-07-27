namespace Harness.DataAccess.Persistence;

public sealed record DatabaseInitializationResult(
    string DatabasePath,
    int SchemaVersion,
    bool DatabaseCreated);
