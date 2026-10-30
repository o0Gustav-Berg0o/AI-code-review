using System.Text;
using DiffPlex;
using DiffPlex.Model;

namespace DiffService.Function;

/// <summary>
/// Builds a unified diff (hunks only, no file headers) shaped like <c>git diff -U{context}</c>.
/// Expects input with LF line endings.
/// </summary>
public static class UnifiedDiffBuilder
{
    // Marks a last line that has no trailing newline. Input with \0 is rejected before this class is called.
    private const char NoEolMarker = '\u0001';
    private const string NoEolText = "\\ No newline at end of file";

    // The diff algorithm costs roughly O(lines * edits): a big file with a few edits takes milliseconds,
    // but a completely rewritten or shuffled one can run for minutes. Two limits keep the worst case bounded.

    /// <summary>Most lines (both sides together, unchanged head and tail excluded) that are handed to the diff algorithm.</summary>
    public const int MaxDiffLines = 60_000;

    /// <summary>
    /// Most lines that differ as a multiset (a cheap lower bound on the number of changed lines).
    /// A change this large is not reviewable anyway.
    /// </summary>
    public const int MaxChangedLines = 20_000;

    /// <exception cref="DiffTooComplexException">The files differ too much to diff within a reasonable time.</exception>
    public static (string Diff, int ChangedLines) Build(string before, string after, int context)
    {
        if (before == after) return ("", 0);

        var a = SplitLines(before);
        var b = SplitLines(after);

        // Unchanged head and tail never take part in the diff, which makes the common case (a few edits in a big file) cheap.
        var head = 0;
        while (head < a.Count && head < b.Count && a[head] == b[head]) head++;
        var tail = 0;
        while (tail < a.Count - head && tail < b.Count - head && a[^(tail + 1)] == b[^(tail + 1)]) tail++;

        var midA = a.GetRange(head, a.Count - head - tail);
        var midB = b.GetRange(head, b.Count - head - tail);
        if (midA.Count + midB.Count > MaxDiffLines || MultisetDifference(midA, midB) > MaxChangedLines)
            throw new DiffTooComplexException();

        var model = new Differ().CreateCustomDiffs(
            Join(midA), Join(midB), ignoreWhiteSpace: false, ignoreCase: false, Chunk);
        var blocks = model.DiffBlocks
            .Select(x => new DiffBlock(x.DeleteStartA + head, x.DeleteCountA, x.InsertStartB + head, x.InsertCountB))
            .ToList();
        if (blocks.Count == 0) return ("", 0);

        var sb = new StringBuilder();
        var changed = 0;
        var i = 0;
        while (i < blocks.Count)
        {
            // Merge blocks into one hunk while the unchanged gap is within 2 * context.
            var j = i;
            while (j + 1 < blocks.Count && Gap(blocks[j], blocks[j + 1]) <= 2 * context) j++;

            var first = blocks[i];
            var last = blocks[j];
            var startA = Math.Max(0, first.DeleteStartA - context);
            var startB = Math.Max(0, first.InsertStartB - context);
            var endA = Math.Min(a.Count, last.DeleteStartA + last.DeleteCountA + context);

            var lines = new StringBuilder();
            var posA = startA;
            var countA = 0;
            var countB = 0;
            for (var k = i; k <= j; k++)
            {
                var blk = blocks[k];
                for (; posA < blk.DeleteStartA; posA++) { AppendLine(lines, ' ', a[posA]); countA++; countB++; }
                for (var d = 0; d < blk.DeleteCountA; d++) { AppendLine(lines, '-', a[posA++]); countA++; changed++; }
                for (var n = 0; n < blk.InsertCountB; n++) { AppendLine(lines, '+', b[blk.InsertStartB + n]); countB++; changed++; }
            }
            for (; posA < endA; posA++) { AppendLine(lines, ' ', a[posA]); countA++; countB++; }

            sb.Append("@@ -").Append(Range(startA, countA)).Append(" +").Append(Range(startB, countB)).Append(" @@\n");
            sb.Append(lines);
            i = j + 1;
        }

        return (sb.ToString().TrimEnd('\n'), changed);
    }

    // Number of lines that cannot be paired with an equal line on the other side, regardless of order.
    private static int MultisetDifference(List<string> a, List<string> b)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in a) counts[line] = counts.GetValueOrDefault(line) + 1;
        foreach (var line in b) counts[line] = counts.GetValueOrDefault(line) - 1;
        return counts.Values.Sum(Math.Abs);
    }

    private static int Gap(DiffBlock x, DiffBlock y) => y.DeleteStartA - (x.DeleteStartA + x.DeleteCountA);

    // Like git: "start,count" with 1-based start, "start" when count is 1, and the preceding line number when count is 0.
    private static string Range(int start0, int count) =>
        count switch
        {
            0 => $"{start0},0",
            1 => $"{start0 + 1}",
            _ => $"{start0 + 1},{count}",
        };

    private static void AppendLine(StringBuilder sb, char prefix, string line)
    {
        sb.Append(prefix);
        if (line.EndsWith(NoEolMarker))
            sb.Append(line, 0, line.Length - 1).Append('\n').Append(NoEolText);
        else
            sb.Append(line);
        sb.Append('\n');
    }

    private static string Join(List<string> lines) => string.Concat(lines.Select(l => l + "\n"));

    private static string[] Chunk(string text) => text.Split('\n')[..^1];

    private static List<string> SplitLines(string text)
    {
        if (text.Length == 0) return [];
        var lines = text.Split('\n').ToList();
        if (lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        else lines[^1] += NoEolMarker;
        return lines;
    }
}

/// <summary>The files differ too much to diff within a reasonable time.</summary>
public class DiffTooComplexException()
    : Exception("The files are too large or differ too much to diff.");
