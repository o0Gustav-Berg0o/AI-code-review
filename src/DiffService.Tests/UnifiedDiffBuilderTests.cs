using DiffService.Function;

namespace DiffService.Tests;

public class UnifiedDiffBuilderTests
{
    private static string Lines(int from, int to) =>
        string.Concat(Enumerable.Range(from, to - from + 1).Select(n => $"line{n}\n"));

    [Fact]
    public void IdenticalFiles_GiveEmptyDiff()
    {
        var (diff, changed) = UnifiedDiffBuilder.Build("a\nb\n", "a\nb\n", 8);
        Assert.Equal("", diff);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void EmptyAgainstEmpty_GivesEmptyDiff()
    {
        var (diff, changed) = UnifiedDiffBuilder.Build("", "", 8);
        Assert.Equal("", diff);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void NewFile_AllLinesAdded()
    {
        var (diff, changed) = UnifiedDiffBuilder.Build("", "a\nb\nc\n", 8);
        Assert.Equal("@@ -0,0 +1,3 @@\n+a\n+b\n+c", diff);
        Assert.Equal(3, changed);
    }

    [Fact]
    public void DeletedFile_AllLinesRemoved()
    {
        var (diff, changed) = UnifiedDiffBuilder.Build("a\nb\n", "", 8);
        Assert.Equal("@@ -1,2 +0,0 @@\n-a\n-b", diff);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void ChangeInTheMiddle_ShowsContextOnBothSides()
    {
        var before = Lines(1, 20);
        var after = before.Replace("line10\n", "CHANGED\n");
        var (diff, changed) = UnifiedDiffBuilder.Build(before, after, 3);

        Assert.Equal(2, changed);
        Assert.StartsWith("@@ -7,7 +7,7 @@\n line7\n line8\n line9\n-line10\n+CHANGED\n line11", diff);
        Assert.EndsWith(" line13", diff);
    }

    [Fact]
    public void ChangeOnFirstAndLastLine_ClampsContextToFile()
    {
        var before = Lines(1, 5);
        var after = before.Replace("line1\n", "FIRST\n").Replace("line5\n", "LAST\n");
        var (diff, changed) = UnifiedDiffBuilder.Build(before, after, 8);

        Assert.Equal(4, changed);
        Assert.Equal("@@ -1,5 +1,5 @@\n-line1\n+FIRST\n line2\n line3\n line4\n-line5\n+LAST", diff);
    }

    [Fact]
    public void CloseChanges_AreMergedIntoOneHunk()
    {
        var before = Lines(1, 30);
        var after = before.Replace("line10\n", "X\n").Replace("line16\n", "Y\n");
        var (diff, _) = UnifiedDiffBuilder.Build(before, after, 3);

        Assert.Equal(1, diff.Split('\n').Count(l => l.StartsWith("@@")));
    }

    [Fact]
    public void DistantChanges_AreSeparateHunks()
    {
        var before = Lines(1, 40);
        var after = before.Replace("line5\n", "X\n").Replace("line30\n", "Y\n");
        var (diff, _) = UnifiedDiffBuilder.Build(before, after, 3);

        Assert.Equal(2, diff.Split('\n').Count(l => l.StartsWith("@@")));
    }

    [Fact]
    public void MissingFinalNewline_IsReportedLikeGit()
    {
        var (diff, changed) = UnifiedDiffBuilder.Build("a\nb", "a\nb\n", 8);
        Assert.Equal("@@ -1,2 +1,2 @@\n a\n-b\n\\ No newline at end of file\n+b", diff);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void LargeFile_IsFast()
    {
        var before = Lines(1, 20_000);
        var after = before.Replace("line10000\n", "CHANGED\n");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (_, changed) = UnifiedDiffBuilder.Build(before, after, 8);
        Assert.Equal(2, changed);
        Assert.True(sw.ElapsedMilliseconds < 2000);
    }
}
