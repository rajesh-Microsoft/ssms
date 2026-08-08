using DevPortal.Api.Models;
using DevPortal.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PortalOptions>(builder.Configuration.GetSection("Portal"));
builder.Services.AddSingleton<IHostRunner, SshHostRunner>();
builder.Services.AddSingleton<IHostRunner, AzRunCommandRunner>();
builder.Services.AddSingleton<IHostRunner, NullHostRunner>();
builder.Services.AddSingleton<EnvironmentStatusService>();

// Health probes hit public hostnames; a slow box must not hold the dashboard open.
builder.Services.AddHttpClient("probe", client => client.Timeout = TimeSpan.FromSeconds(8));

// The portal is served as static files from a different origin during development.
const string PortalCors = "portal";
builder.Services.AddCors(options => options.AddPolicy(PortalCors, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Portal:AllowedOrigins").Get<string[]>() ?? ["http://127.0.0.1:8766", "http://localhost:8766"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors(PortalCors);

// Read-only by design. Deploy and rollback arrive in a later phase, behind real
// authentication — a portal that can deploy is a remote code execution surface.
app.MapGet("/api/meta", () => Results.Ok(new
{
    live = true,
    readOnly = true,
    source = "Read from DEPLOYED_COMMIT / DEPLOYED_VERIFIED and docker ps on each box",
    checkedAt = DateTimeOffset.UtcNow
}));

app.MapGet("/api/applications", (EnvironmentStatusService service) => Results.Ok(service.Applications));

app.MapGet("/api/environments", async (EnvironmentStatusService service, string? app, CancellationToken ct)
    => Results.Ok(await service.GetAllAsync(app, ct)));

app.MapGet("/api/environments/{id}", async (string id, EnvironmentStatusService service, CancellationToken ct)
    => await service.GetAsync(id, ct) is { } status ? Results.Ok(status) : Results.NotFound());

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
