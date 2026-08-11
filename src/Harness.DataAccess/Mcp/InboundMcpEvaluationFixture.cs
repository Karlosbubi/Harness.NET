using System.Security.Cryptography;
using System.Text;
using Harness.DataAccess.Configuration;
using LibGit2Sharp;

namespace Harness.DataAccess.Mcp;

public sealed record InboundMcpEvaluationSnapshot(
    string RootPath,
    string EntryPoint,
    string Head,
    int ChangedFiles,
    IReadOnlyList<string> Paths,
    DateTimeOffset CapturedAt);

public interface IInboundMcpEvaluationFixture
{
    ValueTask<InboundMcpEvaluationSnapshot> EnsureAsync(
        CancellationToken cancellationToken = default);
    ValueTask<InboundMcpEvaluationSnapshot> ResetAsync(
        CancellationToken cancellationToken = default);
    ValueTask<InboundMcpEvaluationSnapshot> SnapshotAsync(
        CancellationToken cancellationToken = default);
}

internal sealed class InboundMcpEvaluationFixture(
    IApplicationPaths paths,
    TimeProvider timeProvider) : IInboundMcpEvaluationFixture
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async ValueTask<InboundMcpEvaluationSnapshot> EnsureAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            string root = Root();
            if (!Repository.IsValid(root)) Create(root);
            return Snapshot(root);
        }
        finally { gate.Release(); }
    }

    public async ValueTask<InboundMcpEvaluationSnapshot> ResetAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            string root = Root();
            if (!Repository.IsValid(root)) Create(root);
            using Repository repository = new(root);
            repository.Reset(ResetMode.Hard, repository.Head.Tip);
            foreach (StatusEntry entry in repository.RetrieveStatus(new StatusOptions
            { IncludeUntracked = true, RecurseUntrackedDirs = true })
                         .Where(item => item.State.HasFlag(FileStatus.NewInWorkdir)))
            {
                string candidate = Path.GetFullPath(Path.Combine(root, entry.FilePath));
                if (candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                    File.Exists(candidate)) File.Delete(candidate);
            }
            return Snapshot(root);
        }
        finally { gate.Release(); }
    }

    public async ValueTask<InboundMcpEvaluationSnapshot> SnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            string root = Root();
            if (!Repository.IsValid(root))
                throw new InvalidOperationException("The isolated evaluation fixture is not initialized.");
            return Snapshot(root);
        }
        finally { gate.Release(); }
    }

    private string Root()
    {
        string data = Path.GetFullPath(paths.Current.DataDirectory);
        string temporary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if (!data.StartsWith(temporary + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Evaluation fixtures are permitted only below the system temporary directory.");
        return Path.Combine(data, "evaluation-fixture");
    }

    private static void Create(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "src", "Fixture"));
        File.WriteAllText(Path.Combine(root, "Fixture.slnx"),
            "<Solution><Project Path=\"src/Fixture/Fixture.csproj\" /></Solution>\n", Encoding.UTF8);
        File.WriteAllText(Path.Combine(root, "src", "Fixture", "Fixture.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup></Project>\n",
            Encoding.UTF8);
        File.WriteAllText(Path.Combine(root, "src", "Fixture", "Counter.cs"),
            "namespace EvaluationFixture;\n\npublic sealed class Counter\n{\n    public int Value { get; private set; }\n    public void Increment() => Value++;\n}\n",
            Encoding.UTF8);
        Repository.Init(root);
        using Repository repository = new(root);
        Commands.Stage(repository, "*");
        Signature signature = new("Harness.NET evaluation", "evaluation@localhost",
            DateTimeOffset.UnixEpoch);
        repository.Commit("Create deterministic evaluation fixture", signature, signature);
    }

    private InboundMcpEvaluationSnapshot Snapshot(string root)
    {
        using Repository repository = new(root);
        string[] tracked = repository.Index.Select(entry => entry.Path)
            .Order(StringComparer.Ordinal).ToArray();
        int changed = repository.RetrieveStatus().Count(item => item.State != FileStatus.Ignored);
        return new(root, Path.Combine(root, "Fixture.slnx"), repository.Head.Tip.Sha,
            changed, tracked, timeProvider.GetUtcNow());
    }
}
