using Harness.DataAccess.Inspection;
using LibGit2Sharp;

namespace Harness.DataAccess.Tests.Inspection;

public sealed class LibGitDeveloperGitRepositoryTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "harness-developer-git-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Stages_and_unstages_exact_path_from_expected_state()
    {
        await InitializeAsync();
        string first = Path.Combine(root, "first.txt");
        string second = Path.Combine(root, "second.txt");
        await File.WriteAllTextAsync(first, "changed first\n");
        await File.WriteAllTextAsync(second, "changed second\n");
        var inspector = new LibGitWorkspaceGitInspector();
        var sut = new LibGitDeveloperGitRepository();
        WorkspaceGitState before = await inspector.InspectAsync(root);

        DeveloperGitIndexResult staged = await sut.UpdateIndexAsync(new(
            root,
            new(before.Fingerprint),
            DeveloperGitIndexOperation.Stage,
            [new("first.txt")]));

        Assert.Null(staged.Error);
        Assert.Equal("first.txt", Assert.Single(staged.AffectedPaths).Value);
        Assert.True(staged.State!.Changes.Single(change => change.Path == "first.txt").IsStaged);
        Assert.False(staged.State.Changes.Single(change => change.Path == "second.txt").IsStaged);

        DeveloperGitIndexResult unstaged = await sut.UpdateIndexAsync(new(
            root,
            new(staged.State.Fingerprint),
            DeveloperGitIndexOperation.Unstage,
            [new("first.txt")]));

        Assert.Null(unstaged.Error);
        Assert.False(unstaged.State!.Changes.Single(change => change.Path == "first.txt").IsStaged);
        Assert.True(unstaged.State.Changes.Single(change => change.Path == "first.txt").IsUnstaged);
    }

    [Fact]
    public async Task Rejects_stale_fingerprint_without_mutating_index()
    {
        await InitializeAsync();
        string first = Path.Combine(root, "first.txt");
        await File.WriteAllTextAsync(first, "first change\n");
        var inspector = new LibGitWorkspaceGitInspector();
        var sut = new LibGitDeveloperGitRepository();
        WorkspaceGitState displayed = await inspector.InspectAsync(root);
        await File.WriteAllTextAsync(first, "later change\n");

        DeveloperGitIndexResult result = await sut.UpdateIndexAsync(new(
            root,
            new(displayed.Fingerprint),
            DeveloperGitIndexOperation.Stage,
            [new("first.txt")]));

        Assert.Equal("git_state_stale", result.ErrorCode);
        Assert.NotNull(result.State);
        Assert.NotEqual(displayed.Fingerprint, result.State.Fingerprint);
        Assert.False(result.State.Changes.Single(change => change.Path == "first.txt").IsStaged);
    }

    [Fact]
    public async Task Stages_and_unstages_one_exact_hunk()
    {
        await InitializeAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "first changed\n");
        var sut = new LibGitDeveloperGitRepository();
        WorkspaceGitState before = await new LibGitWorkspaceGitInspector().InspectAsync(root);
        DeveloperGitPatchUnit stage = Assert.Single(before.PatchUnits!, unit =>
            unit.Path.Value == "first.txt" && unit.Direction == DeveloperGitPatchDirection.Stage &&
            unit.Kind == DeveloperGitPatchKind.Hunk);

        DeveloperGitIndexResult staged = await sut.ApplyPatchAsync(new(
            root, new(before.Fingerprint), stage.Id));

        Assert.Null(staged.Error);
        Assert.True(staged.State!.Changes.Single(change => change.Path == "first.txt").IsStaged);
        Assert.False(staged.State.Changes.Single(change => change.Path == "first.txt").IsUnstaged);
        DeveloperGitPatchUnit unstage = Assert.Single(staged.State.PatchUnits!, unit =>
            unit.Path.Value == "first.txt" && unit.Direction == DeveloperGitPatchDirection.Unstage &&
            unit.Kind == DeveloperGitPatchKind.Hunk);

        DeveloperGitIndexResult restored = await sut.ApplyPatchAsync(new(
            root, new(staged.State.Fingerprint), unstage.Id));

        Assert.Null(restored.Error);
        WorkspaceGitFileChange change = Assert.Single(restored.State!.Changes,
            candidate => candidate.Path == "first.txt");
        Assert.False(change.IsStaged);
        Assert.True(change.IsUnstaged);
    }

    [Fact]
    public async Task Stages_individual_replacement_lines_without_staging_the_other_line()
    {
        await InitializeAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "FIRST\n");
        var sut = new LibGitDeveloperGitRepository();
        WorkspaceGitState before = await new LibGitWorkspaceGitInspector().InspectAsync(root);
        DeveloperGitPatchUnit deletion = Assert.Single(before.PatchUnits!, unit =>
            unit.Path.Value == "first.txt" && unit.Direction == DeveloperGitPatchDirection.Stage &&
            unit.Kind == DeveloperGitPatchKind.Line && unit.Preview.StartsWith("-first", StringComparison.Ordinal));

        DeveloperGitIndexResult result = await sut.ApplyPatchAsync(new(
            root, new(before.Fingerprint), deletion.Id));

        Assert.Null(result.Error);
        Assert.Equal(string.Empty, ReadIndexText("first.txt"));
        Assert.True(result.State!.Changes.Single(change => change.Path == "first.txt").IsStaged);
        Assert.True(result.State.Changes.Single(change => change.Path == "first.txt").IsUnstaged);
        Assert.Contains(result.State.PatchUnits!, unit =>
            unit.Direction == DeveloperGitPatchDirection.Stage && unit.Kind == DeveloperGitPatchKind.Line &&
            unit.Preview.StartsWith("+FIRST", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unstages_individual_replacement_lines_without_unstaging_the_other_line()
    {
        await InitializeAsync();
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "FIRST\n");
        using (Repository repository = new(root)) Commands.Stage(repository, "first.txt");
        var sut = new LibGitDeveloperGitRepository();
        WorkspaceGitState before = await new LibGitWorkspaceGitInspector().InspectAsync(root);
        DeveloperGitPatchUnit addition = Assert.Single(before.PatchUnits!, unit =>
            unit.Path.Value == "first.txt" && unit.Direction == DeveloperGitPatchDirection.Unstage &&
            unit.Kind == DeveloperGitPatchKind.Line && unit.Preview.StartsWith("+FIRST", StringComparison.Ordinal));

        DeveloperGitIndexResult partial = await sut.ApplyPatchAsync(new(
            root, new(before.Fingerprint), addition.Id));

        Assert.Null(partial.Error);
        Assert.Equal(string.Empty, ReadIndexText("first.txt"));
        DeveloperGitPatchUnit deletion = Assert.Single(partial.State!.PatchUnits!, unit =>
            unit.Direction == DeveloperGitPatchDirection.Unstage && unit.Kind == DeveloperGitPatchKind.Line &&
            unit.Preview.StartsWith("-first", StringComparison.Ordinal));
        DeveloperGitIndexResult restored = await sut.ApplyPatchAsync(new(
            root, new(partial.State.Fingerprint), deletion.Id));
        Assert.Null(restored.Error);
        Assert.Equal("first\n", ReadIndexText("first.txt"));
        Assert.False(restored.State!.Changes.Single(change => change.Path == "first.txt").IsStaged);
    }

    [Fact]
    public async Task Rejects_stale_patch_unit_before_index_mutation()
    {
        await InitializeAsync();
        string file = Path.Combine(root, "first.txt");
        await File.WriteAllTextAsync(file, "FIRST\n");
        var sut = new LibGitDeveloperGitRepository();
        WorkspaceGitState displayed = await new LibGitWorkspaceGitInspector().InspectAsync(root);
        DeveloperGitPatchUnit unit = Assert.Single(displayed.PatchUnits!, candidate =>
            candidate.Direction == DeveloperGitPatchDirection.Stage && candidate.Kind == DeveloperGitPatchKind.Hunk);
        await File.WriteAllTextAsync(file, "changed again\n");

        DeveloperGitIndexResult result = await sut.ApplyPatchAsync(new(
            root, new(displayed.Fingerprint), unit.Id));

        Assert.Equal("git_state_stale", result.ErrorCode);
        Assert.Equal("first\n", ReadIndexText("first.txt"));
    }

    [Fact]
    public async Task Patch_units_preserve_git_quoted_unicode_paths()
    {
        await InitializeAsync();
        const string path = "über.txt";
        await File.WriteAllTextAsync(Path.Combine(root, path), "before\n");
        using (Repository repository = new(root))
        {
            Commands.Stage(repository, path);
            Signature signature = new("Harness Tests", "tests@harness.local", DateTimeOffset.UtcNow);
            repository.Commit("unicode path", signature, signature);
        }
        await File.WriteAllTextAsync(Path.Combine(root, path), "after\n");
        WorkspaceGitState before = await new LibGitWorkspaceGitInspector().InspectAsync(root);
        DeveloperGitPatchUnit hunk = Assert.Single(before.PatchUnits!, unit =>
            unit.Path.Value == path && unit.Direction == DeveloperGitPatchDirection.Stage &&
            unit.Kind == DeveloperGitPatchKind.Hunk);

        DeveloperGitIndexResult result = await new LibGitDeveloperGitRepository().ApplyPatchAsync(new(
            root, new(before.Fingerprint), hunk.Id));

        Assert.Null(result.Error);
        Assert.Equal("after\n", ReadIndexText(path));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData(".git/config")]
    [InlineData("/absolute.txt")]
    public async Task Rejects_paths_outside_the_worktree(string path)
    {
        await InitializeAsync();

        DeveloperGitIndexResult result = await new LibGitDeveloperGitRepository().UpdateIndexAsync(new(
            root,
            new("irrelevant"),
            DeveloperGitIndexOperation.Stage,
            [new(path)]));

        Assert.Equal("git_paths_invalid", result.ErrorCode);
    }

    private async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        Repository.Init(root);
        await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "first\n");
        await File.WriteAllTextAsync(Path.Combine(root, "second.txt"), "second\n");
        using Repository repository = new(root);
        Commands.Stage(repository, "*");
        Signature signature = new("Harness Tests", "tests@harness.local", DateTimeOffset.UtcNow);
        repository.Commit("initial", signature, signature);
    }

    private string ReadIndexText(string path)
    {
        using Repository repository = new(root);
        IndexEntry entry = repository.Index[path];
        return repository.Lookup<Blob>(entry.Id).GetContentText();
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
