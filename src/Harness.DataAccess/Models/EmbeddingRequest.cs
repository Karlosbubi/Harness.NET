namespace Harness.DataAccess.Models;

public sealed record EmbeddingRequest(
    string Model,
    IReadOnlyList<string> Inputs,
    int? Dimensions = null);
