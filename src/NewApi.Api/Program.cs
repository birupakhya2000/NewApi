using System.Reflection;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using NewApi.Api.Data;

using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Telemetry is enabled only when a connection string is configured (Azure app setting
// APPLICATIONINSIGHTS_CONNECTION_STRING). Locally and in tests there is none, so we skip it.
var appInsightsConnectionString =
    builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]
    ?? builder.Configuration["ApplicationInsights:ConnectionString"];

if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
{
    builder.Services.AddApplicationInsightsTelemetry();
}
builder.Services.AddProblemDetails();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // 100 requests per minute per client IP, across the whole API
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));
});
builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHealthChecks();

// Read the provider lazily, so test and host configuration overrides are honoured
static bool IsSqlite(IConfiguration config) =>
    string.Equals(config["Database:Provider"] ?? "SqlServer", "Sqlite", StringComparison.OrdinalIgnoreCase);

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var cs = config.GetConnectionString("Default")
             ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

    if (IsSqlite(config))
    {
        options.UseSqlite(cs);
    }
    else
    {
        // Retries cover the free serverless database waking up after auto-pause
        options.UseSqlServer(cs, sql => sql.EnableRetryOnFailure(
            maxRetryCount: 6,
            maxRetryDelay: TimeSpan.FromSeconds(15),
            errorNumbersToAdd: null));
    }
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
// Create or upgrade the database at startup (fine for a single-instance learning app)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (IsSqlite(app.Configuration))
    {
        var csb = new SqliteConnectionStringBuilder(db.Database.GetConnectionString());
        if (!string.IsNullOrEmpty(csb.DataSource) && csb.DataSource != ":memory:")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(csb.DataSource))!);
        }

        db.Database.EnsureCreated();   // local dev and tests: no migrations needed
    }
    else
    {
        db.Database.Migrate();         // Azure SQL: apply the real migrations
    }
}

// API docs: on in Development, or anywhere EnableApiDocs=true
var enableDocs = app.Environment.IsDevelopment()
                 || app.Configuration.GetValue<bool>("EnableApiDocs");

if (enableDocs)
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/", () => enableDocs ? Results.Redirect("/scalar") : Results.Ok("NewApi is running"))
   .ExcludeFromDescription();

app.MapHealthChecks("/health").ExcludeFromDescription();
app.MapGet("/health/ready", async (AppDbContext db, CancellationToken ct) =>
        await db.Database.CanConnectAsync(ct)
            ? Results.Ok(new { status = "Ready" })
            : Results.Problem("Database unavailable", statusCode: StatusCodes.Status503ServiceUnavailable))
    .ExcludeFromDescription();

// Version: which build is running? The pipeline stamps 1.0.<run_number>+<commit sha>.
app.MapGet("/version", () =>
{
    var version = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    return Results.Ok(new { version });
}).ExcludeFromDescription();

// ---------- Todos ----------
var todos = app.MapGroup("/api/todos").WithTags("Todos");

todos.MapGet("/", async (AppDbContext db) =>
{
    var items = await db.Todos.AsNoTracking()
        .OrderBy(t => t.Id)
        .Select(t => new TodoResponse(t.Id, t.Title, t.IsDone, t.CreatedAtUtc))
        .ToListAsync();

    return Results.Ok(items);
});

todos.MapGet("/{id:int}", async (int id, AppDbContext db) =>
{
    var t = await db.Todos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    return t is null ? Results.NotFound() : Results.Ok(new TodoResponse(t.Id, t.Title, t.IsDone, t.CreatedAtUtc));
});

todos.MapPost("/", async (CreateTodoRequest req, AppDbContext db, TimeProvider clock) =>
{
    var title = req.Title?.Trim() ?? "";
    if (title.Length is 0 or > 200)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["title"] = ["Title is required and must be at most 200 characters."]
        });
    }

    var todo = new TodoItem { Title = title, CreatedAtUtc = clock.GetUtcNow() };
    db.Todos.Add(todo);
    await db.SaveChangesAsync();

    return Results.Created($"/api/todos/{todo.Id}",
        new TodoResponse(todo.Id, todo.Title, todo.IsDone, todo.CreatedAtUtc));
});

todos.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
{
    var todo = await db.Todos.FirstOrDefaultAsync(x => x.Id == id);
    if (todo is null) return Results.NotFound();

    db.Todos.Remove(todo);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.Run();

public record CreateTodoRequest(string? Title);
public record TodoResponse(int Id, string Title, bool IsDone, DateTimeOffset CreatedAtUtc);

// Lets the test project start the app with WebApplicationFactory
public partial class Program;