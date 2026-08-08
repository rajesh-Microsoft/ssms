using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DevPortal.Api.Models;
using Microsoft.Extensions.Options;

namespace DevPortal.Api.Services;

public record JobLine(DateTimeOffset At, string Text);

public class DeploymentJob
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Kind { get; init; } = "deploy";        // deploy | rollback
    public string EnvironmentId { get; init; } = "";
    public string Tier { get; init; } = "";
    public string? Commit { get; init; }
    public string StartedBy { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>running | succeeded | failed</summary>
    public string Status { get; set; } = "running";
    public string? Error { get; set; }
    public List<JobLine> Output { get; } = new();
}

/// <summary>
/// Runs deployments by invoking deploy/promote.ps1 — the same script an operator would run.
/// The portal deliberately does not reimplement deployment: promote.ps1 already enforces a
/// clean tree, that the commit is on origin/main, and the previous environment's sign-off.
/// A second implementation would drift from those gates.
/// </summary>
public partial class DeploymentService(
    IOptions<PortalOptions> options,
    IEnumerable<IHostRunner> runners,
    IConfiguration config,
    ILogger<DeploymentService> log)
{
    private readonly PortalOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, DeploymentJob> _jobs = new();

    // One deployment at a time: concurrent runs against the same box interleave output
    // and can leave a half-extracted tree.
    private readonly SemaphoreSlim _gate = new(1, 1);

    [GeneratedRegex("^[0-9a-fA-F]{7,40}$")]
    private static partial Regex ShaPattern();

    [GeneratedRegex("\u001b\\[[0-9;]*[a-zA-Z]")]
    private static partial Regex AnsiPattern();

    private string RepoPath => config["Portal:RepoPath"] ?? Directory.GetCurrentDirectory();
    private string AuditPath => config["Portal:AuditLogPath"] ?? Path.Combine(RepoPath, "devportal", "audit.log");

    public DeploymentJob? GetJob(string id) => _jobs.TryGetValue(id, out var job) ? job : null;
    public IEnumerable<DeploymentJob> RecentJobs() => _jobs.Values.OrderByDescending(j => j.StartedAt).Take(20);

    public void Audit(string actor, string action, string detail, string outcome)
    {
        var line = JsonSerializer.Serialize(new
        {
            at = DateTimeOffset.UtcNow,
            actor,
            action,
            detail,
            outcome
        });
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AuditPath)!);
            File.AppendAllText(AuditPath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            // An unwritable audit log must be loud: it is the only record of who did what.
            log.LogError(ex, "could not write the audit log at {Path}", AuditPath);
        }
    }

    public IReadOnlyList<string> ReadAudit(int count)
    {
        try
        {
            if (!File.Exists(AuditPath)) return Array.Empty<string>();
            return File.ReadLines(AuditPath).TakeLast(count).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public (bool Ok, string? Error, EnvironmentConfig? Env) Validate(string envId, string? commit, bool requireCommit)
    {
        var env = _options.Environments.FirstOrDefault(e => e.Id == envId);
        if (env is null) return (false, "Unknown environment.", null);
        if (env.Hazard) return (false, "That host is blocked and is not a deployment target.", null);
        if (!env.DeploymentEnabled) return (false, $"Deployment is disabled for {env.Name}.", null);
        if (string.IsNullOrWhiteSpace(env.PromoteTarget))
            return (false, $"{env.Name} has no promote target configured.", null);

        if (requireCommit)
        {
            // Never interpolate a caller's string into a command line unchecked.
            if (string.IsNullOrWhiteSpace(commit) || !ShaPattern().IsMatch(commit))
                return (false, "That does not look like a commit sha.", null);
        }
        return (true, null, env);
    }

    public async Task<DeploymentJob> StartDeployAsync(EnvironmentConfig env, string commit, Principal who, CancellationToken ct)
    {
        var job = new DeploymentJob
        {
            Kind = "deploy",
            EnvironmentId = env.Id,
            Tier = env.Tier,
            Commit = commit,
            StartedBy = who.Username
        };
        _jobs[job.Id] = job;

        _ = Task.Run(async () =>
        {
            if (!await _gate.WaitAsync(TimeSpan.FromMinutes(10), CancellationToken.None))
            {
                Finish(job, "failed", "another deployment is already running");
                return;
            }

            try
            {
                Append(job, $"$ pwsh -File deploy/promote.ps1 -Environment {env.PromoteTarget} -Commit {commit}");
                var exit = await RunProcessAsync(
                    "pwsh",
                    $"-NoProfile -File \"{Path.Combine(RepoPath, "deploy", "promote.ps1")}\" -Environment {env.PromoteTarget} -Commit {commit}",
                    RepoPath, job, CancellationToken.None);

                // promote.ps1 throws on a failed gate, so a non-zero exit is a real refusal.
                if (exit == 0) Finish(job, "succeeded", null);
                else Finish(job, "failed", $"promote.ps1 exited with {exit}");
            }
            catch (Exception ex)
            {
                Finish(job, "failed", ex.Message);
            }
            finally
            {
                _gate.Release();
                Audit(who.Username, "deploy", $"{env.Id} {commit}", job.Status);
            }
        }, CancellationToken.None);

        return job;
    }

    public async Task<DeploymentJob> StartRollbackAsync(EnvironmentConfig env, Principal who, CancellationToken ct)
    {
        var job = new DeploymentJob
        {
            Kind = "rollback",
            EnvironmentId = env.Id,
            Tier = env.Tier,
            StartedBy = who.Username
        };
        _jobs[job.Id] = job;

        _ = Task.Run(async () =>
        {
            if (!await _gate.WaitAsync(TimeSpan.FromMinutes(10), CancellationToken.None))
            {
                Finish(job, "failed", "another deployment is already running");
                return;
            }

            try
            {
                var runner = runners.FirstOrDefault(r => r.Type == env.Probe.Type);
                if (runner is null || runner.Type == "none")
                {
                    Finish(job, "failed", "this environment cannot be reached to roll back");
                    return;
                }

                // Re-tag the newest rollback image and recreate the API container. The image
                // is whatever the previous deploy tagged, so this is the documented path.
                const string template = """
                    #!/bin/bash
                    set -e
                    cd __ROOT__
                    TAG=$(docker images --format '{{.Repository}}:{{.Tag}}' | grep '^__IMAGE__:rollback-' | sort | tail -n 1)
                    if [ -z "$TAG" ]; then echo "__ERR__ no rollback image found for __IMAGE__"; exit 1; fi
                    echo "rolling back to $TAG"
                    docker tag "$TAG" __IMAGE__:latest
                    docker compose up -d --force-recreate __SERVICE__
                    docker ps --filter label=com.docker.compose.project=__PROJECT__ --format '  {{.Names}} {{.Status}}'
                    echo "__END__"
                    """;

                var script = template
                    .Replace("__ROOT__", env.Probe.Root ?? "")
                    .Replace("__IMAGE__", env.Image ?? "")
                    .Replace("__SERVICE__", env.ApiService ?? "smms-api")
                    .Replace("__PROJECT__", env.Probe.ComposeProject ?? "");

                Append(job, $"$ rollback on {env.Name}");
                var result = await runner.RunAsync(env.Probe, script, CancellationToken.None);
                foreach (var line in (result.Stdout ?? "").Split('\n').Where(l => l.Trim().Length > 0))
                    Append(job, line.TrimEnd());

                if (!result.Ok) Finish(job, "failed", result.Error ?? "the rollback command failed");
                else if (!(result.Stdout ?? "").Contains("__END__")) Finish(job, "failed", "the rollback did not run to completion");
                else Finish(job, "succeeded", null);
            }
            catch (Exception ex)
            {
                Finish(job, "failed", ex.Message);
            }
            finally
            {
                _gate.Release();
                Audit(who.Username, "rollback", env.Id, job.Status);
            }
        }, CancellationToken.None);

        return job;
    }

    private static void Append(DeploymentJob job, string text)
    {
        var clean = AnsiPattern().Replace(text, "");
        lock (job.Output) job.Output.Add(new JobLine(DateTimeOffset.UtcNow, clean));
    }

    private void Finish(DeploymentJob job, string status, string? error)
    {
        job.Status = status;
        job.Error = error;
        job.FinishedAt = DateTimeOffset.UtcNow;
        if (error is not null) Append(job, $"ERROR: {error}");
        log.LogInformation("job {Id} {Status}", job.Id, status);
    }

    private static async Task<int> RunProcessAsync(string file, string args, string workingDirectory, DeploymentJob job, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file)
        {
            Arguments = args,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Without this PowerShell writes colour escapes and the job log fills with \e[31;1m.
        psi.Environment["NO_COLOR"] = "1";
        psi.Environment["TERM"] = "dumb";

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) Append(job, e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Append(job, e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        await process.WaitForExitAsync(timeout.Token);
        return process.ExitCode;
    }
}
