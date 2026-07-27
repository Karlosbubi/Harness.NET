namespace Harness.DataAccess.Models;

public sealed record ProviderUsage(
    int InputTokens,
    int OutputTokens,
    MicroUsd? Cost = null);
