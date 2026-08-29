using Harness.DataAccess.Configuration;
using Harness.DataAccess.Inspection;
using LibGit2Sharp;

namespace Harness.DataAccess.Tests.Inspection;

public sealed partial class LibGitDeveloperGitRepositoryTests
{
    private async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        Repository.Init(root);
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "first\n");
        await File.WriteAllTextAsync(Path.Combine(root, "second.txt"), "second\n");
        using Repository repository = new(root);
        repository.Config.Set("user.name", "Harness Tests");
        repository.Config.Set("user.email", "tests@harness.local");
        Commands.Stage(repository, "*");
        Signature signature = new("Harness Tests", "tests@harness.local", DateTimeOffset.UtcNow);
        repository.Commit("initial", signature, signature);
    }

    private async Task CreateConflictAsync()
    {
        await InitializeAsync();
        using Repository repository = new(root);
        Signature signature = new("Harness Tests", "tests@harness.local", DateTimeOffset.UtcNow);
        string originalBranch = repository.Head.FriendlyName;
        Branch branch = repository.CreateBranch("conflicting-branch");
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "main version\n");
        Commands.Stage(repository, "first.txt");
        repository.Commit("main change", signature, signature);
        Commands.Checkout(repository, branch);
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "branch version\n");
        Commands.Stage(repository, "first.txt");
        repository.Commit("branch change", signature, signature);
        Commands.Checkout(repository, originalBranch);
        MergeResult result = repository.Merge(branch, signature);
        Assert.Equal(MergeStatus.Conflicts, result.Status);
    }

    private string NewWorktreePath()
    {
        string path = root + "-linked-" + Guid.NewGuid().ToString("N");
        linkedWorktreePaths.Add(path);
        return path;
    }

    private sealed class StubApplicationPaths(ApplicationPaths current) : IApplicationPaths
    {
        public ApplicationPaths Current { get; } = current;
    }

    private string ReadIndexText(string path)
    {
        using Repository repository = new(root);
        IndexEntry entry = repository.Index[path];
        return repository.Lookup<Blob>(entry.Id).GetContentText();
    }

    public void Dispose()
    {
        foreach (string path in linkedWorktreePaths)
            TestDirectoryCleanup.Delete(path);
        TestDirectoryCleanup.Delete(root);
    }
}
