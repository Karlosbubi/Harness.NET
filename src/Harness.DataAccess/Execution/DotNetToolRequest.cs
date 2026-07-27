namespace Harness.DataAccess.Execution;

public sealed record DotNetToolRequest(
    string Operation,
    string EntryPoint);
