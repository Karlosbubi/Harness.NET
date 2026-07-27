namespace Harness.DataAccess.Execution;

public sealed record DotNetToolRequest(
    DotNetToolOperation Operation,
    string EntryPoint);
