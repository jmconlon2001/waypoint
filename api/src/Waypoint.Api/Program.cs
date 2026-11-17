using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Waypoint.Api.Data;
using Waypoint.Api.Endpoints;
using Waypoint.Api.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Waypoint")
    ?? throw new InvalidOperationException("Connection string 'Waypoint' is not configured.");

builder.Services.AddDbContext<WaypointDbContext>(options => options
    .UseNpgsql(connectionString)
    // The initial migration was written without the EF tooling; log (don't throw) if the snapshot drifts.
    .ConfigureWarnings(w => w.Log(RelationalEventId.PendingModelChangesWarning)));

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<PeopleService>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<TimerService>();
builder.Services.AddScoped<TimeEntryService>();
builder.Services.AddScoped<InterruptionService>();
builder.Services.AddScoped<ReviewService>();

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Dev only: the Angular dev server (ng serve) proxies /api, so CORS is just a fallback for direct calls.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins("http://localhost:4200", "http://127.0.0.1:4200")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, title) = error switch
    {
        ApiException api => (api.StatusCode, api.Message),
        BadHttpRequestException bad => (bad.StatusCode, "The request was not valid. Check the values and try again."),
        _ => (StatusCodes.Status500InternalServerError, "Something went wrong on the server."),
    };
    context.Response.StatusCode = status;
    await Results.Problem(title: title, statusCode: status).ExecuteAsync(context);
}));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors();
}

// Apply migrations and seed defaults at startup (single-user local app).
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WaypointDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await db.Database.MigrateAsync();
            await DbSeeder.SeedAsync(db);
            break;
        }
        catch (Exception ex) when (attempt < 10 && ex is Npgsql.NpgsqlException or System.Net.Sockets.SocketException or TimeoutException)
        {
            logger.LogWarning("Database not ready ({Message}); retrying in 3s (attempt {Attempt}/10)", ex.Message, attempt);
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }
}

app.MapWaypointApi();

app.Run();

public partial class Program;
