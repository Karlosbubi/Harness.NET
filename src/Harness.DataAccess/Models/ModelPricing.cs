namespace Harness.DataAccess.Models;

public sealed record ModelPricing(
    decimal InputUsdPerToken,
    decimal OutputUsdPerToken,
    decimal UsdPerRequest);
