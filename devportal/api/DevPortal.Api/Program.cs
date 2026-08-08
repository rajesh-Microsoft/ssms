using DevPortal.Api.Models;
using DevPortal.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Secrets arrive as a mounted file, not environment variables: a PBKDF2 hash contains '$'
// and Docker Compose interpolates those out of an env_file, silently truncating it.
builder.Configuration.AddJsonFile("appsettings.Secrets.json", optional: true, reloadOnChange: false);

// `dotnet run -- hash <password>` prints a hash for appsettings, so a plaintext
// password never has to be written down anywhere.
if (args.Length == 2 && args[0] == "hash")
{
    Console.WriteLine(AuthService.HashPassword(args[1]));
    return;
}

builder.Services.Configure<PortalOptions>(builder.Configuration.GetSection("Portal"));
builder.Services.AddSingleton<IHostRunner, SshHostRunner>();
builder.Services.AddSingleton<IHostRunner, AzRunCommandRunner>();
builder.Services.AddSingleton<IHostRunner, NullHostRunner>();
builder.Services.AddSingleton<IHostRunner, LocalHostRunner>();
builder.Services.AddSingleton<GitService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<DeploymentService>();
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

// Two switches the hosted copy relies on. Both default to the safe answer: a deployment
// button has to be turned on deliberately, because forgetting the flag on an
// internet-facing host is the failure that matters.
var allowWrites = builder.Configuration.GetValue("Portal:AllowWrites", false);
var requireAuthForReads = builder.Configuration.GetValue("Portal:RequireAuthForReads", true);

Principal? CurrentUser(HttpContext http, AuthService auth)
{
    var header = http.Request.Headers.Authorization.ToString();
    var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..] : null;
    return auth.Validate(token);
}

IResult? ReadGuard(HttpContext http, AuthService auth)
    => requireAuthForReads && CurrentUser(http, auth) is null
        ? Results.Json(new { message = "Sign in first." }, statusCode: 401)
        : null;

app.MapGet("/api/meta", (AuthService auth) => Results.Ok(new
{
    live = true,
    readOnly = !auth.IsConfigured || !allowWrites,
    authConfigured = auth.IsConfigured,
    writesAllowed = allowWrites,
    notConfiguredReason = auth.IsConfigured ? null : auth.NotConfiguredReason,
    source = "Read from DEPLOYED_COMMIT / DEPLOYED_VERIFIED and docker ps on each box",
    checkedAt = DateTimeOffset.UtcNow
}));

app.MapPost("/api/auth/login", (LoginRequest request, AuthService auth, DeploymentService deployments) =>
{
    if (!auth.IsConfigured) return Results.Json(new { message = auth.NotConfiguredReason }, statusCode: 503);

    var principal = auth.Authenticate(request.Username ?? "", request.Password ?? "");
    if (principal is null)
    {
        deployments.Audit(request.Username ?? "(unknown)", "login", "-", "refused");
        return Results.Json(new { message = "Wrong username or password." }, statusCode: 401);
    }

    deployments.Audit(principal.Username, "login", "-", "ok");
    return Results.Ok(new
    {
        token = auth.IssueToken(principal),
        username = principal.Username,
        name = principal.Name,
        role = principal.Role,
        deploy = auth.AllowedTiers(principal),
        rollback = auth.CanRollback(principal)
    });
});

app.MapGet("/api/applications", (HttpContext http, AuthService auth, EnvironmentStatusService service)
    => ReadGuard(http, auth) ?? Results.Ok(service.Applications));

app.MapGet("/api/environments", async (HttpContext http, AuthService auth, EnvironmentStatusService service, string? app, CancellationToken ct)
    => ReadGuard(http, auth) ?? Results.Ok(await service.GetAllAsync(app, ct)));

app.MapGet("/api/environments/{id}", async (string id, HttpContext http, AuthService auth, EnvironmentStatusService service, CancellationToken ct)
    => ReadGuard(http, auth) ?? (await service.GetAsync(id, ct) is { } status ? Results.Ok(status) : Results.NotFound()));

// Recent commits on the branch, i.e. what could be deployed.
app.MapGet("/api/builds", async (HttpContext http, AuthService auth, GitService git, CancellationToken ct)
    => ReadGuard(http, auth) ?? Results.Ok(await git.GetRecentAsync(15, ct)));

// Deployment history is per box: each deploy appends a line to DEPLOYED_HISTORY.
// Boxes deployed before that existed simply have a shorter history, which is stated
// rather than padded out.
app.MapGet("/api/deployments", async (HttpContext http, AuthService auth, EnvironmentStatusService service, string? app, CancellationToken ct) =>
{
    if (ReadGuard(http, auth) is { } denied) return denied;
    var environments = await service.GetAllAsync(app, ct);
    var records = environments
        .SelectMany(e => e.History.Select(h => new { environment = e.Name, tier = e.Tier, record = h }))
        .OrderByDescending(x => x.record.At)
        .ToList();
    return Results.Ok(records);
});

app.MapGet("/api/repository", async (HttpContext http, AuthService auth, GitService git, IConfiguration cfg, CancellationToken ct)
    => ReadGuard(http, auth) ?? Results.Ok(new
    {
        name = cfg["Portal:RepositoryName"] ?? "unknown",
        url = cfg["Portal:RepositoryUrl"],
        branch = await git.GetBranchAsync(ct) ?? "unknown"
    }));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// ── Write endpoints ─────────────────────────────────────────
// Every one of these requires a token, checks the role against the tier on the server,
// and records the attempt whether it succeeds or is refused.

app.MapPost("/api/deployments", async (DeployRequest request, HttpContext http, AuthService auth,
    DeploymentService deployments, CancellationToken ct) =>
{
    if (!allowWrites) return Results.Json(new { message = "This portal is running read-only. Deploy from a workstation." }, statusCode: 403);
    if (!auth.IsConfigured) return Results.Json(new { message = auth.NotConfiguredReason }, statusCode: 503);

    var who = CurrentUser(http, auth);
    if (who is null) return Results.Json(new { message = "Sign in first." }, statusCode: 401);

    var (ok, error, env) = deployments.Validate(request.EnvironmentId ?? "", request.Commit, requireCommit: true);
    if (!ok || env is null)
    {
        deployments.Audit(who.Username, "deploy", $"{request.EnvironmentId} {request.Commit}", $"refused: {error}");
        return Results.BadRequest(new { message = error });
    }

    if (!auth.CanDeploy(who, env.Tier))
    {
        deployments.Audit(who.Username, "deploy", $"{env.Id} {request.Commit}", "refused: role");
        return Results.Json(new { message = $"Your role ({who.Role}) cannot deploy to {env.Tier}." }, statusCode: 403);
    }

    // Anything above DEV must be typed out in full, so it cannot be a stray click.
    if (env.Tier != "DEV" && !string.Equals(request.Confirm, env.Name, StringComparison.OrdinalIgnoreCase))
    {
        deployments.Audit(who.Username, "deploy", $"{env.Id} {request.Commit}", "refused: confirmation");
        return Results.BadRequest(new { message = $"Type the environment name exactly ({env.Name}) to confirm." });
    }

    var job = await deployments.StartDeployAsync(env, request.Commit!, who, ct);
    return Results.Accepted($"/api/jobs/{job.Id}", new { jobId = job.Id });
});

app.MapPost("/api/deployments/rollback", async (RollbackRequest request, HttpContext http, AuthService auth,
    DeploymentService deployments, CancellationToken ct) =>
{
    if (!allowWrites) return Results.Json(new { message = "This portal is running read-only. Roll back from a workstation." }, statusCode: 403);
    if (!auth.IsConfigured) return Results.Json(new { message = auth.NotConfiguredReason }, statusCode: 503);

    var who = CurrentUser(http, auth);
    if (who is null) return Results.Json(new { message = "Sign in first." }, statusCode: 401);

    if (!auth.CanRollback(who))
    {
        deployments.Audit(who.Username, "rollback", request.EnvironmentId ?? "-", "refused: role");
        return Results.Json(new { message = $"Your role ({who.Role}) cannot roll back." }, statusCode: 403);
    }

    var (ok, error, env) = deployments.Validate(request.EnvironmentId ?? "", null, requireCommit: false);
    if (!ok || env is null)
    {
        deployments.Audit(who.Username, "rollback", request.EnvironmentId ?? "-", $"refused: {error}");
        return Results.BadRequest(new { message = error });
    }

    if (!string.Equals(request.Confirm, env.Name, StringComparison.OrdinalIgnoreCase))
    {
        deployments.Audit(who.Username, "rollback", env.Id, "refused: confirmation");
        return Results.BadRequest(new { message = $"Type the environment name exactly ({env.Name}) to confirm." });
    }

    var job = await deployments.StartRollbackAsync(env, who, ct);
    return Results.Accepted($"/api/jobs/{job.Id}", new { jobId = job.Id });
});

app.MapGet("/api/jobs/{id}", (string id, HttpContext http, AuthService auth, DeploymentService deployments) =>
{
    if (CurrentUser(http, auth) is null) return Results.Json(new { message = "Sign in first." }, statusCode: 401);
    var job = deployments.GetJob(id);
    if (job is null) return Results.NotFound();

    lock (job.Output)
    {
        return Results.Ok(new
        {
            job.Id,
            job.Kind,
            job.EnvironmentId,
            job.Tier,
            job.Commit,
            job.StartedBy,
            job.StartedAt,
            job.FinishedAt,
            job.Status,
            job.Error,
            output = job.Output.Select(l => l.Text).ToList()
        });
    }
});

app.MapGet("/api/audit", (HttpContext http, AuthService auth, DeploymentService deployments) =>
{
    var who = CurrentUser(http, auth);
    if (who is null) return Results.Json(new { message = "Sign in first." }, statusCode: 401);
    return Results.Ok(deployments.ReadAudit(100));
});

app.Run();

record LoginRequest(string? Username, string? Password);
record DeployRequest(string? EnvironmentId, string? Commit, string? Confirm);
record RollbackRequest(string? EnvironmentId, string? Confirm);
