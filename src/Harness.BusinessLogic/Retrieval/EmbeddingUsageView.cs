using Harness.BusinessLogic.Costs;

namespace Harness.BusinessLogic.Retrieval;

public sealed record EmbeddingUsageView(
    int InputTokens,
    MicroUsdAmount? Cost);
