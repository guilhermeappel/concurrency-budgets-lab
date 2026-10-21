using Lab.Stubs;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var app = builder.Build();
var state = new StubState();

app.MapPost("/scenario/{name}", (string name) =>
    state.TrySetScenario(name) ? Results.NoContent() : Results.BadRequest($"Unknown scenario '{name}'."));

app.MapPost("/reset", () =>
{
    state.Reset();
    return Results.NoContent();
});

app.MapGet("/stats", () => Results.Ok(state.Snapshot()));

app.MapGet("/enrich/{itemId:int}/{part:int}", async (int itemId, int part, CancellationToken cancellationToken) =>
{
    state.EnterEnrichment();
    try
    {
        await Task.Delay(state.NextEnrichmentDelay(), cancellationToken);
        return Results.Ok(new { itemId, part, value = itemId * 31 + part });
    }
    catch (OperationCanceledException)
    {
        // The client gave up (its timeout fired). Nothing useful to send back.
        return Results.Empty;
    }
    finally
    {
        state.ExitEnrichment();
    }
});

app.MapPost("/records/{itemId:int}", async (int itemId, bool complete, CancellationToken cancellationToken) =>
{
    if (!state.TryEnterRecords())
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    try
    {
        await state.ProcessRecordAsync(cancellationToken);
        return Results.NoContent();
    }
    catch (OperationCanceledException)
    {
        return Results.Empty;
    }
    finally
    {
        state.ExitRecords();
    }
});

app.Run(Environment.GetEnvironmentVariable("STUBS_URL") ?? "http://0.0.0.0:5080");
