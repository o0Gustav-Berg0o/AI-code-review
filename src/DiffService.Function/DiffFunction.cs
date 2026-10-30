using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DiffService.Function;

public record DiffRequest(string? Before, string? After, int? Context);
public record DiffResponse(string Diff, int ChangedLines, bool Skipped, string? Reason);

public class DiffFunction(ILogger<DiffFunction> logger)
{
    private const int MaxChars = 500_000;
    private const int DefaultContext = 8;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Function("Diff")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "diff")] HttpRequest req)
    {
        DiffRequest? input;
        try { input = await req.ReadFromJsonAsync<DiffRequest>(JsonOptions); }
        catch (JsonException) { return new BadRequestObjectResult("Invalid JSON"); }

        return Handle(input);
    }

    public IActionResult Handle(DiffRequest? input)
    {
        var before = Normalize(input?.Before);
        var after = Normalize(input?.After);
        var context = Math.Clamp(input?.Context ?? DefaultContext, 0, 50);

        if (before.Contains('\0') || after.Contains('\0'))
            return new OkObjectResult(new DiffResponse("", 0, true, "binary"));
        if (before.Length > MaxChars || after.Length > MaxChars)
            return new OkObjectResult(new DiffResponse("", 0, true, "too large"));

        var (diff, changed) = UnifiedDiffBuilder.Build(before, after, context);

        // Logga aldrig innehåll, bara storlekar.
        logger.LogInformation("Diff built. BeforeLen={B} AfterLen={A} Changed={C}",
            before.Length, after.Length, changed);

        return new OkObjectResult(new DiffResponse(diff, changed, false, null));
    }

    private static string Normalize(string? s) => (s ?? "").Replace("\r\n", "\n");
}
