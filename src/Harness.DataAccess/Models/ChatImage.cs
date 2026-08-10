namespace Harness.DataAccess.Models;

public sealed record ChatImageMediaType(string Value);

public sealed record ChatImageBase64(string Value);

public sealed record ChatImage(
    ChatImageMediaType MediaType,
    ChatImageBase64 Base64);
