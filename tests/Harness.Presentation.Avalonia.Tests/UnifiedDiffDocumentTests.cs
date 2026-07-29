namespace Harness.Presentation.Avalonia.Tests;

public sealed class UnifiedDiffDocumentTests
{
    private const string SampleDiff = """
        diff --git a/src/Sample.cs b/src/Sample.cs
        index 1a2b3c4..5d6e7f8 100644
        --- a/src/Sample.cs
        +++ b/src/Sample.cs
        @@ -10,7 +10,8 @@ public sealed class Sample
             public int Value { get; }

        -    public Sample(int value)
        +    public Sample(int value, string name)
             {
                 Value = value;
        +        Name = name;
             }
        """;

    [Fact]
    public void Empty_input_produces_an_empty_document()
    {
        Assert.True(UnifiedDiffDocument.Parse(null).IsEmpty);
        Assert.True(UnifiedDiffDocument.Parse(string.Empty).IsEmpty);
        Assert.Equal("No textual changes.", UnifiedDiffDocument.Parse(null).Summary);
    }

    [Fact]
    public void Parsing_classifies_lines_and_counts_changes()
    {
        UnifiedDiffDocument document = UnifiedDiffDocument.Parse(SampleDiff);

        Assert.Equal(1, document.FileCount);
        Assert.Equal(2, document.AddedCount);
        Assert.Equal(1, document.RemovedCount);
        Assert.Contains("+2", document.Summary, StringComparison.Ordinal);
        Assert.Contains("−1", document.Summary, StringComparison.Ordinal);
        Assert.Single(document.Lines, line => line.Kind == DiffLineKind.FileHeader);
        Assert.Single(document.Lines, line => line.Kind == DiffLineKind.HunkHeader);
    }

    [Fact]
    public void Parsing_strips_markers_so_content_is_not_doubled()
    {
        UnifiedDiffDocument document = UnifiedDiffDocument.Parse(SampleDiff);

        DiffLine added = document.Lines.First(line => line.Kind == DiffLineKind.Added);
        Assert.StartsWith("    public Sample(int value, string name)", added.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            document.Lines.Where(line => line.Kind is DiffLineKind.Added or DiffLineKind.Removed),
            line => line.Text.StartsWith('+') || line.Text.StartsWith('-'));
    }

    [Fact]
    public void Line_numbers_follow_the_hunk_header()
    {
        UnifiedDiffDocument document = UnifiedDiffDocument.Parse(SampleDiff);

        DiffLine firstContext = document.Lines.First(line => line.Kind == DiffLineKind.Context);
        Assert.Equal(10, firstContext.OldLine);
        Assert.Equal(10, firstContext.NewLine);

        DiffLine removed = document.Lines.First(line => line.Kind == DiffLineKind.Removed);
        Assert.NotNull(removed.OldLine);
        Assert.Null(removed.NewLine);

        DiffLine added = document.Lines.First(line => line.Kind == DiffLineKind.Added);
        Assert.Null(added.OldLine);
        Assert.NotNull(added.NewLine);
    }

    [Fact]
    public void Metadata_lines_are_not_mistaken_for_additions_or_removals()
    {
        UnifiedDiffDocument document = UnifiedDiffDocument.Parse(SampleDiff);

        // "--- a/..." and "+++ b/..." start with - and + but are metadata, not changes.
        Assert.Equal(2, document.AddedCount);
        Assert.Equal(1, document.RemovedCount);
        Assert.Contains(document.Lines, line => line is { Kind: DiffLineKind.Meta, Text: "--- a/src/Sample.cs" });
        Assert.Contains(document.Lines, line => line is { Kind: DiffLineKind.Meta, Text: "+++ b/src/Sample.cs" });
    }

    [Fact]
    public void Side_by_side_pairs_replacements_and_keeps_columns_aligned()
    {
        UnifiedDiffDocument document = UnifiedDiffDocument.Parse(SampleDiff);

        IReadOnlyList<DiffRow> rows = document.ToSideBySideRows();

        // The single replaced constructor line pairs removed-left with added-right.
        DiffRow replacement = rows.First(row =>
            row.Left?.Kind == DiffLineKind.Removed && row.Right?.Kind == DiffLineKind.Added);
        Assert.Contains("int value)", replacement.Left!.Text, StringComparison.Ordinal);
        Assert.Contains("string name)", replacement.Right!.Text, StringComparison.Ordinal);

        // The unpaired added line leaves the left column empty rather than shifting rows.
        Assert.Contains(rows, row => row.Left is null && row.Right?.Kind == DiffLineKind.Added);
        Assert.All(rows, row => Assert.False(row.Left is null && row.Right is null));
    }

    [Fact]
    public void Side_by_side_shares_context_and_header_rows_across_both_columns()
    {
        IReadOnlyList<DiffRow> rows = UnifiedDiffDocument.Parse(SampleDiff).ToSideBySideRows();

        DiffRow header = rows.First(row => row.Left?.Kind == DiffLineKind.FileHeader);
        Assert.Same(header.Left, header.Right);
    }

    [Fact]
    public void An_unbalanced_removal_block_leaves_the_added_column_empty()
    {
        UnifiedDiffDocument document = UnifiedDiffDocument.Parse("""
            diff --git a/a.txt b/a.txt
            @@ -1,3 +1,1 @@
            -one
            -two
            +only
            """);

        IReadOnlyList<DiffRow> rows = document.ToSideBySideRows();

        Assert.Equal(2, document.RemovedCount);
        Assert.Equal(1, document.AddedCount);
        Assert.Contains(rows, row => row.Left?.Text == "two" && row.Right is null);
    }

    [Fact]
    public void Binary_and_rename_metadata_survive_without_being_counted()
    {
        UnifiedDiffDocument document = UnifiedDiffDocument.Parse("""
            diff --git a/logo.png b/brand.png
            similarity index 100%
            rename from logo.png
            rename to brand.png
            Binary files a/logo.png and b/brand.png differ
            """);

        Assert.Equal(0, document.AddedCount);
        Assert.Equal(0, document.RemovedCount);
        Assert.Equal(1, document.FileCount);
        Assert.All(
            document.Lines.Skip(1),
            line => Assert.Equal(DiffLineKind.Meta, line.Kind));
    }

    [Fact]
    public void Multiple_files_are_counted_independently()
    {
        UnifiedDiffDocument document = UnifiedDiffDocument.Parse("""
            diff --git a/a.cs b/a.cs
            @@ -1 +1 @@
            -a
            +b
            diff --git a/c.cs b/c.cs
            @@ -1 +1 @@
            -c
            +d
            """);

        Assert.Equal(2, document.FileCount);
        Assert.Equal(2, document.AddedCount);
        Assert.Equal(2, document.RemovedCount);
    }

    [Fact]
    public void Windows_line_endings_do_not_create_phantom_lines()
    {
        UnifiedDiffDocument document = UnifiedDiffDocument.Parse(
            "diff --git a/a.cs b/a.cs\r\n@@ -1 +1 @@\r\n-old\r\n+new\r\n");

        Assert.Equal(1, document.AddedCount);
        Assert.Equal(1, document.RemovedCount);
        Assert.DoesNotContain(document.Lines, line => line.Text.Contains('\r', StringComparison.Ordinal));
    }
}
