using System.Diagnostics;

namespace DevPortal.Api.Services;

public record CommitInfo(string Sha, string ShortSha, string Subject, string Author, DateTimeOffset? Date);

/// <summary>
/// Reads the local clone for the metadata a box does not keep. A box records the sha it
/// is running and nothing else, so the message and author come from git.
/// </summary>
public class GitService(IConfiguration config, ILogger<GitService> log)
{
    private readonly string _repo = config["Portal:RepoPath"] ?? Directory.GetCurrentDirectory();

    public async Task<CommitInfo?> GetCommitAsync(string? sha, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sha)) return null;
        // %x1f is the unit separator: subjects contain spaces and sometimes colons.
        var output = await RunAsync($"show -s --format=%H%x1f%h%x1f%s%x1f%an%x1f%aI {sha}", ct);
        if (output is null) return null;

        var parts = output.Trim().Split('\u001f');
        if (parts.Length < 5) return null;

        DateTimeOffset? date = DateTimeOffset.TryParse(parts[4], out var parsed) ? parsed : null;
        return new CommitInfo(parts[0], parts[1], parts[2], parts[3], date);
    }

    public async Task<IReadOnlyList<CommitInfo>> GetRecentAsync(int count, CancellationToken ct)
    {
        var output = await RunAsync($"log -n {count} --format=%H%x1f%h%x1f%s%x1f%an%x1f%aI", ct);
        if (output is null) return Array.Empty<CommitInfo>();

        var list = new List<CommitInfo>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split('\u001f');
            if (parts.Length < 5) continue;
            DateTimeOffset? date = DateTimeOffset.TryParse(parts[4], out var parsed) ? parsed : null;
            list.Add(new CommitInfo(parts[0], parts[1], parts[2], parts[3], date));
        }
        return list;
    }

    /// <summary>How far an environment has fallen behind the branch. Null when the sha is
    /// not in this clone, which is itself worth knowing.</summary>
    public async Task<int?> GetCommitsBehindAsync(string? sha, string reference, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sha)) return null;
        var output = await RunAsync($"rev-list --count {sha}..{reference}", ct);
        return int.TryParse(output?.Trim(), out var n) ? n : null;
    }

    public async Task<string?> GetBranchAsync(CancellationToken ct)
        => (await RunAsync("rev-parse --abbrev-ref HEAD", ct))?.Trim();

    private async Task<string?> RunAsync(string args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = _repo,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return null;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            var stdout = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            // A sha that is not in this clone exits non-zero; that is a fact, not a failure.
            return process.ExitCode == 0 ? stdout : null;
        }
        catch (Exception ex)
        {
            log.LogWarning("git {Args} failed: {Message}", args, ex.Message);
            return null;
        }
    }
}
