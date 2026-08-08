using System.Diagnostics;
using System.Text;
using DevPortal.Api.Models;

namespace DevPortal.Api.Services;

public record ShellResult(bool Ok, string Stdout, string? Error);

/// <summary>Runs a read-only shell snippet on the box that hosts an environment.</summary>
public interface IHostRunner
{
    string Type { get; }
    Task<ShellResult> RunAsync(ProbeConfig probe, string script, CancellationToken ct);
}

internal static class ProcessRunner
{
    public static async Task<ShellResult> RunAsync(string file, string args, string? stdin, int timeoutSeconds, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file)
        {
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null) return new ShellResult(false, "", $"could not start {file}");

            if (stdin is not null)
            {
                await process.StandardInput.WriteAsync(stdin.Replace("\r", ""));
                process.StandardInput.Close();
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            var outText = await stdout;
            var errText = await stderr;

            // A non-zero exit is common here (a stray CR on the last line of a piped
            // script), so trust the marker the script prints, not the exit code.
            return new ShellResult(true, outText, string.IsNullOrWhiteSpace(errText) ? null : errText.Trim());
        }
        catch (OperationCanceledException)
        {
            return new ShellResult(false, "", $"{file} timed out after {timeoutSeconds}s");
        }
        catch (Exception ex)
        {
            return new ShellResult(false, "", ex.Message);
        }
    }
}

/// <summary>Reaches a box over SSH. On this estate SSH is JIT-gated, so a timeout here
/// usually means the access window has lapsed rather than the box being down.</summary>
public class SshHostRunner(ILogger<SshHostRunner> log) : IHostRunner
{
    public string Type => "ssh";

    public async Task<ShellResult> RunAsync(ProbeConfig probe, string script, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(probe.Host) || string.IsNullOrWhiteSpace(probe.User))
            return new ShellResult(false, "", "ssh probe is missing Host or User");

        var key = string.IsNullOrWhiteSpace(probe.KeyPath) ? "" : $"-i \"{probe.KeyPath}\" ";
        var args = $"{key}-o ConnectTimeout=10 -o BatchMode=yes -o StrictHostKeyChecking=accept-new {probe.User}@{probe.Host} \"bash -s\"";

        var result = await ProcessRunner.RunAsync("ssh", args, script, 25, ct);
        if (!result.Ok) log.LogWarning("ssh probe to {Host} failed: {Error}", probe.Host, result.Error);
        return result;
    }
}

/// <summary>Reaches a box through the Azure agent. Pre-prod blocks SSH from workstations,
/// so this is the only way in.</summary>
public class AzRunCommandRunner(ILogger<AzRunCommandRunner> log) : IHostRunner
{
    public string Type => "az";

    public async Task<ShellResult> RunAsync(ProbeConfig probe, string script, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(probe.VmName) || string.IsNullOrWhiteSpace(probe.ResourceGroup))
            return new ShellResult(false, "", "az probe is missing VmName or ResourceGroup");

        var temp = Path.Combine(Path.GetTempPath(), $"devportal-probe-{Guid.NewGuid():N}.sh");
        await File.WriteAllTextAsync(temp, script.Replace("\r", ""), new UTF8Encoding(false), ct);

        try
        {
            var sub = string.IsNullOrWhiteSpace(probe.Subscription) ? "" : $"--subscription {probe.Subscription} ";
            var args = $"vm run-command invoke {sub}-g {probe.ResourceGroup} -n {probe.VmName} " +
                       $"--command-id RunShellScript --scripts \"@{temp}\" -o json";

            // On Windows `az` is a batch shim around Python. Started directly it dies with
            // "Failed to load python executable", so it has to go through cmd.exe.
            var (exe, fullArgs) = OperatingSystem.IsWindows()
                ? ("cmd.exe", $"/c az {args}")
                : ("az", args);

            var result = await ProcessRunner.RunAsync(exe, fullArgs, null, 90, ct);
            if (!result.Ok)
            {
                log.LogWarning("az run-command on {Vm} failed: {Error}", probe.VmName, result.Error);
                return result;
            }

            // run-command wraps output in JSON with a "[stdout]\n...\n[stderr]" message.
            var stdout = ExtractStdout(result.Stdout);
            return result with { Stdout = stdout };
        }
        finally
        {
            try { File.Delete(temp); } catch { /* best effort */ }
        }
    }

    private static string ExtractStdout(string raw)
    {
        var start = raw.IndexOf("[stdout]", StringComparison.Ordinal);
        if (start < 0) return raw;
        start += "[stdout]".Length;
        var end = raw.IndexOf("[stderr]", start, StringComparison.Ordinal);
        var body = end < 0 ? raw[start..] : raw[start..end];
        return body.Replace("\\n", "\n").Replace("\\r", "").Trim();
    }
}

public class NullHostRunner : IHostRunner
{
    public string Type => "none";
    public Task<ShellResult> RunAsync(ProbeConfig probe, string script, CancellationToken ct)
        => Task.FromResult(new ShellResult(false, "", "no probe is configured for this environment"));
}

/// <summary>Runs the probe on the machine hosting this API. Used when the portal is
/// deployed onto the same box as the stacks it reports on, so no SSH key has to be
/// copied anywhere.</summary>
public class LocalHostRunner(ILogger<LocalHostRunner> log) : IHostRunner
{
    public string Type => "local";

    public async Task<ShellResult> RunAsync(ProbeConfig probe, string script, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("/bin/bash", "-s", script, 25, ct);
        if (!result.Ok) log.LogWarning("local probe failed: {Error}", result.Error);
        return result;
    }
}
