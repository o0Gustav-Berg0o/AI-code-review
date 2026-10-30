using DiffService.Function;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiffService.Tests;

public class DiffFunctionTests
{
    private readonly DiffFunction _function = new(NullLogger<DiffFunction>.Instance);

    private DiffResponse Call(string? before, string? after, int? context = null)
    {
        var result = Assert.IsType<OkObjectResult>(_function.Handle(new DiffRequest(before, after, context)));
        return Assert.IsType<DiffResponse>(result.Value);
    }

    [Fact]
    public void CrlfAgainstLf_GivesEmptyDiff()
    {
        var r = Call("a\r\nb\r\n", "a\nb\n");
        Assert.Equal("", r.Diff);
        Assert.Equal(0, r.ChangedLines);
        Assert.False(r.Skipped);
    }

    [Fact]
    public void NullInputs_AreTreatedAsEmpty()
    {
        var r = Call(null, null);
        Assert.Equal("", r.Diff);
        Assert.False(r.Skipped);
    }

    [Fact]
    public void BinaryContent_IsSkipped()
    {
        var r = Call("a\0b", "a");
        Assert.True(r.Skipped);
        Assert.Equal("binary", r.Reason);
    }

    [Fact]
    public void TooLargeContent_IsSkipped()
    {
        var r = Call(new string('x', 500_001), "a");
        Assert.True(r.Skipped);
        Assert.Equal("too large", r.Reason);
    }

    [Fact]
    public void Context_IsClampedTo0Through50()
    {
        var before = string.Concat(Enumerable.Range(1, 200).Select(n => $"l{n}\n"));
        var after = before.Replace("l100\n", "X\n");

        Assert.Equal(2, Call(before, after, 0).Diff.Split('\n').Length - 1);
        // 50 lines of context on each side plus -/+ and the header
        Assert.Equal(1 + 50 + 2 + 50, Call(before, after, 999).Diff.Split('\n').Length);
    }

    [Fact]
    public void NullRequest_DoesNotThrow()
    {
        var result = Assert.IsType<OkObjectResult>(_function.Handle(null));
        Assert.IsType<DiffResponse>(result.Value);
    }
}
