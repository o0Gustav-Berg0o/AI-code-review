using System.Text;
using DiffService.Function;
using Microsoft.AspNetCore.Http;
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
    public void MixedLineEndings_OnlyRealChangesShowUp()
    {
        var r = Call("a\r\nb\nc\r\nd\n", "a\nb\r\nX\nd\r\n", 1);
        Assert.Equal("@@ -2,3 +2,3 @@\n b\n-c\n+X\n d", r.Diff);
        Assert.Equal(2, r.ChangedLines);
    }

    [Fact]
    public void ExactlyAtSizeLimit_IsNotSkipped()
    {
        var r = Call(new string('x', 500_000), "a");
        Assert.False(r.Skipped);
        Assert.Equal(2, r.ChangedLines);
    }

    [Fact]
    public void RewrittenLargeFile_IsSkippedAsTooComplex()
    {
        var before = string.Concat(Enumerable.Range(0, 30_000).Select(n => $"a{n}\n"));
        var after = string.Concat(Enumerable.Range(0, 30_000).Select(n => $"b{n}\n"));
        Assert.True(before.Length < 500_000 && after.Length < 500_000);

        var r = Call(before, after);
        Assert.True(r.Skipped);
        Assert.Equal("too complex", r.Reason);
        Assert.Equal("", r.Diff);
    }

    private static DefaultHttpContext RequestWith(string body, string? contentType)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        ctx.Request.ContentType = contentType;
        return ctx;
    }

    [Fact]
    public async Task Run_ValidJson_ReturnsDiff()
    {
        var ctx = RequestWith("""{"before":"a\nb\n","after":"a\nc\n","context":3}""", "application/json");
        var result = Assert.IsType<OkObjectResult>(await _function.Run(ctx.Request));
        var response = Assert.IsType<DiffResponse>(result.Value);

        Assert.Equal("@@ -1,2 +1,2 @@\n a\n-b\n+c", response.Diff);
        Assert.Equal(2, response.ChangedLines);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("""{"before": 123}""")]
    [InlineData("""{"before": "a",""")]
    public async Task Run_InvalidJson_Returns400(string body)
    {
        var ctx = RequestWith(body, "application/json");
        Assert.IsType<BadRequestObjectResult>(await _function.Run(ctx.Request));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("text/plain")]
    public async Task Run_WithoutJsonContentType_StillWorks(string? contentType)
    {
        var ctx = RequestWith("""{"before":"a\n","after":"b\n"}""", contentType);
        var result = Assert.IsType<OkObjectResult>(await _function.Run(ctx.Request));
        Assert.Equal(2, Assert.IsType<DiffResponse>(result.Value).ChangedLines);
    }

    [Fact]
    public void NullRequest_DoesNotThrow()
    {
        var result = Assert.IsType<OkObjectResult>(_function.Handle(null));
        Assert.IsType<DiffResponse>(result.Value);
    }
}
