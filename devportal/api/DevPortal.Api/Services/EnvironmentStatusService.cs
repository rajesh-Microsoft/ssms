using System.Collections.Concurrent;
using DevPortal.Api.Models;
using Microsoft.Extensions.Options;

namespace DevPortal.Api.Services;

/// <summary>
/// Reads the state each box already keeps: DEPLOYED_COMMIT and DEPLOYED_VERIFIED written
/// by deploy/promote.ps1, plus `docker ps`. Nothing is written and no second source of
/// truth is invented — if a value cannot be read it stays null and ProbeState says why.
/// </summary>
public class EnvironmentStatusService(
    IOptions<PortalOptions> options,
    IEnumerable<IHostRunner> runners,
    IHttpClientFactory httpFactory,
    GitService git,
    IConfiguration config,
    ILogger<EnvironmentStatusService> log)
{
    private readonly PortalOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, EnvironmentStatus Status)> _cache = new();

    public IReadOnlyList<ApplicationConfig> Applications => _options.Applications;

    public async Task<IReadOnlyList<EnvironmentStatus>> GetAllAsync(string? app, CancellationToken ct)
    {
        var wanted = _options.Environments.Where(e => app is null || e.App == app);
        var tasks = wanted.Select(e => GetAsync(e.Id, ct));
        return (await Task.WhenAll(tasks)).Where(s => s is not null).Select(s => s!).ToList();
    }

    public async Task<EnvironmentStatus?> GetAsync(string id, CancellationToken ct)
    {
        var config = _options.Environments.FirstOrDefault(e => e.Id == id);
        if (config is null) return null;

        if (_cache.TryGetValue(id, out var cached) &&
            DateTimeOffset.UtcNow - cached.At < TimeSpan.FromSeconds(_options.CacheSeconds))
        {
            return cached.Status;
        }

        var status = await BuildAsync(config, ct);
        _cache[id] = (DateTimeOffset.UtcNow, status);
        return status;
    }

    private async Task<EnvironmentStatus> BuildAsync(EnvironmentConfig config_, CancellationToken ct)
    {
        var health = await ProbeHttpAsync(config_, ct);
        var (probeState, error, commit, verified, containers, history) = await ProbeHostAsync(config_, ct);

        var reference = config["Portal:ReferenceBranch"] ?? "origin/main";
        var info = await git.GetCommitAsync(commit, ct);
        var behind = await git.GetCommitsBehindAsync(commit, reference, ct);
        var enriched = await EnrichHistoryAsync(history, ct);

        return new EnvironmentStatus
        {
            Id = config_.Id,
            App = config_.App,
            Tier = config_.Tier,
            Name = config_.Name,
            Hostname = config_.Hostname,
            AppUrl = config_.AppUrl,
            ApiUrl = config_.ApiUrl,
            DataSensitivity = config_.DataSensitivity,
            DeploymentEnabled = config_.DeploymentEnabled,
            Hazard = config_.Hazard,
            Note = config_.Note,
            Image = config_.Image,
            Pipeline = config_.Pipeline,
            Database = config_.Database,
            Server = config_.Server,
            Commit = commit,
            VerifiedCommit = verified,
            CommitMessage = info?.Subject,
            CommitAuthor = info?.Author,
            CommitDate = info?.Date,
            CommitsBehind = behind,
            History = enriched,
            Containers = containers,
            SiteHttpCode = health.Site,
            ApiHttpCode = health.Api,
            Health = health.Summary,
            ProbeState = probeState,
            ProbeError = error,
            CheckedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<List<DeploymentRecord>> EnrichHistoryAsync(List<DeploymentRecord> history, CancellationToken ct)
    {
        var result = new List<DeploymentRecord>();
        foreach (var record in history)
        {
            var info = await git.GetCommitAsync(record.Commit, ct);
            result.Add(record with { Message = info?.Subject, ShortCommit = info?.ShortSha ?? record.ShortCommit });
        }
        return result;
    }

    private async Task<(int? Site, int? Api, string Summary)> ProbeHttpAsync(EnvironmentConfig config, CancellationToken ct)
    {
        var client = httpFactory.CreateClient("probe");
        var site = await CodeAsync(client, config.AppUrl, ct);
        var api = await CodeAsync(client, config.ApiUrl, ct);

        // The site root is static nginx, so it answers even when the API is down.
        // Treat a gateway code (or nothing at all) as the only real failure signal.
        var summary = site switch
        {
            null => "Unknown",
            >= 500 and < 600 => "Unhealthy",
            _ => api is null or (>= 500 and < 600) ? "Site up, API not answering" : "Healthy"
        };
        if (config.Hazard) summary = "Blocked";
        return (site, api, summary);
    }

    private static async Task<int?> CodeAsync(HttpClient client, string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            return (int)response.StatusCode;
        }
        catch
        {
            return null;
        }
    }

    private async Task<(string State, string? Error, string? Commit, string? Verified, List<ContainerStatus> Containers, List<DeploymentRecord> History)>
        ProbeHostAsync(EnvironmentConfig config, CancellationToken ct)
    {
        var runner = runners.FirstOrDefault(r => r.Type == config.Probe.Type) ?? runners.First(r => r.Type == "none");
        if (runner.Type == "none")
            return ("unavailable", "no probe is configured for this environment", null, null, new(), new());

        var root = config.Probe.Root ?? "";
        var project = config.Probe.ComposeProject ?? "";

        // Placeholders rather than interpolation: docker's --format braces collide
        // with C# interpolation, and escaping them is how this gets silently wrong.
        //
        // Containers are selected by compose project label, not by name substring:
        // `--filter name=smms-` also matches the smms-dev-* containers sharing the box.
        const string template = """
            #!/bin/bash
            cd __ROOT__ 2>/dev/null || { echo "__ERR__ root __ROOT__ not found"; exit 0; }
            echo "__COMMIT__ $(cat DEPLOYED_COMMIT 2>/dev/null | tr -d '\r\n')"
            echo "__VERIFIED__ $(cat DEPLOYED_VERIFIED 2>/dev/null | tr -d '\r\n')"
            docker ps --filter label=com.docker.compose.project=__PROJECT__ --format '__CONTAINER__ {{.Names}}|{{.Status}}' 2>/dev/null
            tail -n 15 DEPLOYED_HISTORY 2>/dev/null | sed 's/^/__HISTORY__ /'
            echo "__END__"
            """;

        var script = template.Replace("__ROOT__", root).Replace("__PROJECT__", project);

        var result = await runner.RunAsync(config.Probe, script, ct);
        if (!result.Ok)
            return ("unavailable", result.Error ?? "probe failed", null, null, new(), new());

        if (!result.Stdout.Contains("__END__"))
        {
            log.LogWarning("probe for {Id} did not complete: {Out}", config.Id, Trim(result.Stdout));
            return ("unavailable", "the probe did not run to completion", null, null, new(), new());
        }

        string? commit = null, verified = null;
        var containers = new List<ContainerStatus>();
        var history = new List<DeploymentRecord>();
        string? err = null;

        foreach (var line in result.Stdout.Split('\n').Select(l => l.Trim()))
        {
            if (line.StartsWith("__ERR__")) err = line["__ERR__".Length..].Trim();
            else if (line.StartsWith("__COMMIT__")) commit = Blank(line["__COMMIT__".Length..].Trim());
            else if (line.StartsWith("__VERIFIED__")) verified = Blank(line["__VERIFIED__".Length..].Trim());
            else if (line.StartsWith("__CONTAINER__"))
            {
                var parts = line["__CONTAINER__".Length..].Trim().Split('|', 2);
                if (parts.Length == 2) containers.Add(new ContainerStatus(parts[0], parts[1]));
            }
            else if (line.StartsWith("__HISTORY__"))
            {
                // sha|iso8601|who|environment
                var parts = line["__HISTORY__".Length..].Trim().Split('|');
                if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0])) continue;
                DateTimeOffset? at = DateTimeOffset.TryParse(parts[1], out var parsed) ? parsed : null;
                history.Add(new DeploymentRecord(
                    parts[0],
                    parts[0].Length >= 7 ? parts[0][..7] : parts[0],
                    at,
                    parts.Length > 2 ? parts[2] : "—",
                    parts.Length > 3 ? parts[3] : config.Tier,
                    null));
            }
        }

        if (err is not null) return ("unavailable", err, null, null, new(), new());
        history.Reverse();
        return ("ok", null, commit, verified, containers, history);
    }

    private static string? Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
    private static string Trim(string s) => s.Length <= 300 ? s : s[..300];
}
