using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.Json;
using Npgsql;
using Ticketnauta.WebMcp.Contracts;
using Ticketnauta.WebMcp.Data;
using Ticketnauta.WebMcp.Domain;
using Ticketnauta.WebMcp.Options;

if (args.Contains("--health-check", StringComparer.Ordinal))
{
    Environment.ExitCode = await RunContainerHealthCheckAsync();
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

var connectionString = builder.Configuration.GetConnectionString("DemoDatabase");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings__DemoDatabase is required. Copy .env.example to .env and use Docker Compose, or set the environment variable locally.");
}

var demoOptions = new DemoOptions
{
    HoldMinutes = builder.Configuration.GetValue<int?>("Demo:HoldMinutes") ?? 10,
    AdminToken = builder.Configuration["Demo:AdminToken"] ?? string.Empty
};
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.ConnectionStringBuilder.ApplicationName = "Ticketnauta AI Seat Concierge";

builder.Services.AddSingleton(demoOptions);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(dataSourceBuilder.Build());
builder.Services.AddSingleton<DatabaseInitializer>();
builder.Services.AddSingleton<DemoRepository>();

var app = builder.Build();
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ApiErrors");

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Permissions-Policy"] = "tools=(self)";
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'";
        return Task.CompletedTask;
    });

    try
    {
        await next(context);
    }
    catch (SeatConflictException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = ex.StatusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = $"https://ticketnauta.demo/problems/{ex.ErrorCode}",
            title = "seat availability changed",
            status = ex.StatusCode,
            detail = ex.Message,
            code = ex.ErrorCode,
            unavailableSeatIds = ex.UnavailableSeatIds,
            recovery = new
            {
                nextTool = "find_seat_options",
                instruction = "Refresh availability, rank replacements, explain what changed, and obtain renewed user approval before selecting or holding a replacement."
            },
            traceId = context.TraceIdentifier
        }, context.RequestAborted);
    }
    catch (ApiException ex) when (!context.Response.HasStarted)
    {
        context.Response.StatusCode = ex.StatusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = $"https://ticketnauta.demo/problems/{ex.ErrorCode}",
            title = ex.ErrorCode.Replace('_', ' '),
            status = ex.StatusCode,
            detail = ex.Message,
            code = ex.ErrorCode,
            traceId = context.TraceIdentifier
        }, context.RequestAborted);
    }
    catch (Exception ex) when (!context.Response.HasStarted)
    {
        logger.LogError(ex, "Unhandled error for {Method} {Path}", context.Request.Method, context.Request.Path);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://ticketnauta.demo/problems/internal_error",
            title = "internal error",
            status = 500,
            detail = "The demo could not complete the request.",
            code = "internal_error",
            traceId = context.TraceIdentifier
        }, context.RequestAborted);
    }
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        // Assets are intentionally small and not fingerprinted in this demo.
        // Revalidate them so a redeploy never leaves judges with stale WebMCP code or CSS.
        context.Context.Response.Headers.CacheControl = "no-cache";
    }
});

app.MapGet("/health/live", () => Results.Ok(new
{
    status = "live",
    service = "ticketnauta-webmcp-demo"
}));

app.MapGet("/health/ready", async (DemoRepository repository, CancellationToken cancellationToken) =>
    await repository.IsReadyAsync(cancellationToken)
        ? Results.Ok(new { status = "ready", database = "webmcp_demo" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

var api = app.MapGroup("/api");

api.MapGet("/events", async (
    string? query,
    DemoRepository repository,
    CancellationToken cancellationToken) =>
{
    var events = await repository.SearchEventsAsync(query, cancellationToken);
    return Results.Ok(new { events, count = events.Count });
});

api.MapGet("/events/{eventId}", async (
    string eventId,
    string? sessionId,
    DemoRepository repository,
    CancellationToken cancellationToken) =>
{
    var details = await repository.GetEventDetailsAsync(eventId, sessionId, cancellationToken);
    return Results.Ok(details);
});

api.MapPost("/events/{eventId}/seat-options", async (
    string eventId,
    FindSeatOptionsRequest request,
    DemoRepository repository,
    CancellationToken cancellationToken) =>
{
    var options = await repository.FindSeatOptionsAsync(eventId, request, cancellationToken);
    return Results.Ok(new
    {
        eventId,
        options,
        count = options.Count,
        message = options.Count == 0
            ? "No safe seat combination matches those constraints. Try a larger budget, fewer seats, or explicitly allow a 2 + 2 fallback for a group of four."
            : options.Any(option => option.Layout == "split_2_plus_2")
                ? $"Found {options.Count} ranked two-pair fallback alternatives because no four-seat contiguous block matched."
                : $"Found {options.Count} ranked contiguous alternatives with explainable score evidence."
    });
});

api.MapPost("/cart/select", async (
    SelectSeatOptionRequest request,
    DemoRepository repository,
    CancellationToken cancellationToken) =>
{
    var cart = await repository.SelectSeatOptionAsync(request, cancellationToken);
    return Results.Ok(cart);
});

api.MapGet("/cart/{sessionId}", async (
    string sessionId,
    DemoRepository repository,
    CancellationToken cancellationToken) =>
{
    var cart = await repository.GetCartSummaryAsync(sessionId, cancellationToken);
    return Results.Ok(cart);
});

api.MapPost("/holds", async (
    HoldSeatsRequest request,
    DemoRepository repository,
    CancellationToken cancellationToken) =>
{
    var cart = await repository.HoldSeatsAsync(request, cancellationToken);
    return Results.Created($"/api/holds/{cart.Hold!.Id}", cart);
});

api.MapDelete("/holds/{holdId:guid}", async (
    Guid holdId,
    string sessionId,
    DemoRepository repository,
    CancellationToken cancellationToken) =>
{
    var cart = await repository.ReleaseHoldAsync(holdId, sessionId, cancellationToken);
    return Results.Ok(cart);
});

api.MapPost("/checkout", async (
    CheckoutRequest request,
    DemoRepository repository,
    CancellationToken cancellationToken) =>
{
    var checkout = await repository.CheckoutAsync(request.SessionId, cancellationToken);
    return Results.Ok(checkout);
});

api.MapPost("/demo/competing-hold", async (
    SimulateCompetingBuyerRequest request,
    DemoRepository repository,
    CancellationToken cancellationToken) =>
{
    var disruption = await repository.SimulateCompetingBuyerAsync(request, cancellationToken);
    return Results.Created($"/api/holds/{disruption.HoldId}", disruption);
});

api.MapPost("/demo/session-reset", async (
    DemoSessionResetRequest request,
    DemoRepository repository,
    CancellationToken cancellationToken) =>
{
    await repository.ResetSessionDemoAsync(request.SessionId, cancellationToken);
    return Results.Ok(new
    {
        reset = true,
        sessionId = request.SessionId,
        message = "This browser session, its simulated competitor, and their fictional runtime data were reset. Seed data and other sessions were preserved."
    });
});

api.MapPost("/demo/reset", async (
    HttpContext context,
    DemoResetRequest request,
    DemoOptions options,
    DemoRepository repository,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(options.AdminToken))
    {
        return Results.Problem(
            title: "Reset is disabled",
            detail: "Set Demo__AdminToken to enable the demo reset endpoint.",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var suppliedToken = context.Request.Headers["X-Demo-Admin-Token"].ToString();
    if (!FixedTimeEquals(suppliedToken, options.AdminToken))
    {
        return Results.Unauthorized();
    }

    if (!string.Equals(request.Confirmation, "RESET_DEMO", StringComparison.Ordinal))
    {
        throw new DemoValidationException("Confirmation must be exactly RESET_DEMO.");
    }

    await repository.ResetDemoAsync(cancellationToken);
    return Results.Ok(new
    {
        reset = true,
        schema = "webmcp_demo",
        message = "Demo holds, carts, and simulated checkouts were cleared; seed data remains."
    });
});

app.MapFallbackToFile("index.html");

await app.Services.GetRequiredService<DatabaseInitializer>()
    .InitializeAsync(app.Lifetime.ApplicationStopping);
await app.RunAsync();

static bool FixedTimeEquals(string supplied, string expected)
{
    var left = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
    var right = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
    return CryptographicOperations.FixedTimeEquals(left, right);
}

static async Task<int> RunContainerHealthCheckAsync()
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        using var response = await client.GetAsync("http://127.0.0.1:8080/health/ready");
        return response.IsSuccessStatusCode ? 0 : 1;
    }
    catch
    {
        return 1;
    }
}
