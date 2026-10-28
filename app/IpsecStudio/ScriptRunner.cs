using System.Diagnostics;
using System.Text;

namespace IpsecStudio;

// Boundary between orchestration (testable) and the machine (PowerShell 5.1 under the process identity).
public interface IScriptRunner {
    Task<(bool Ok, string Output)> Run(string script, TimeSpan timeout);
}
public sealed class PowerShellRunner(ILogger<PowerShellRunner> logger) : IScriptRunner {
    public async Task<(bool Ok, string Output)> Run(string script, TimeSpan timeout) {
        var file = Path.Combine(Path.GetTempPath(), "ipsec-studio-" + Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(file, script, new UTF8Encoding(true));
        try {
            var start = new ProcessStartInfo {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", file }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start PowerShell.");
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(timeout);
            try { await process.WaitForExitAsync(cts.Token); } catch (OperationCanceledException) { try { process.Kill(true); } catch { } return (false, L.T("deploy.timeout")); }
            var output = (await stdout).Trim(); var error = (await stderr).Trim();
            if (process.ExitCode != 0) logger.LogWarning("Script failed: {Error}", error);
            return (process.ExitCode == 0, process.ExitCode == 0 ? output : (error.Length > 0 ? error : output));
        } finally { File.Delete(file); }
    }
}
