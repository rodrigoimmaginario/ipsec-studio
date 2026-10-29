using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IpsecStudio;

public sealed class DirectoryReader(IConfiguration config, IWebHostEnvironment environment, ILogger<DirectoryReader> logger) {
    private readonly SemaphoreSlim gate = new(1, 1);
    public InventorySnapshot? Current { get; private set; }
    public string? LastError { get; private set; }
    // Empty = the domain DNS name: the AD cmdlets then locate any reachable DC (recommended with several DCs).
    public string Server => string.IsNullOrWhiteSpace(config["Directory:Server"]) ? Domain : config["Directory:Server"]!.Trim();
    public string Domain => config["Directory:ExpectedDomain"] ?? throw new InvalidOperationException("Domain is required.");
    public async Task<InventorySnapshot> Refresh(CancellationToken cancellation) {
        if (!Regex.IsMatch(Server, @"\A[a-zA-Z0-9.-]{1,253}\z") || !Regex.IsMatch(Domain, @"\A[a-zA-Z0-9.-]{1,253}\z"))
            throw new InvalidOperationException("Invalid directory configuration.");
        await gate.WaitAsync(cancellation);
        try {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(config.GetValue("Directory:TimeoutSeconds", 90), 5, 180)));
            var start = new ProcessStartInfo {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, CreateNoWindow = true
            };
            foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(environment.ContentRootPath, "scripts", "Read-Inventory.ps1"), "-Server", Server, "-ExpectedDomain", Domain }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start read-only collector.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } throw; }
            var error = await stderr;
            if (process.ExitCode != 0) { logger.LogWarning("Directory collector failed: {Error}", error); throw new IOException("Directory collector failed."); }
            var snapshot = JsonSerializer.Deserialize<InventorySnapshot>((await stdout).Trim('\uFEFF', '\r', '\n', ' '), Json.Settings) ?? throw new IOException("Empty inventory.");
            if (!snapshot.Domain.Equals(Domain, StringComparison.OrdinalIgnoreCase) || snapshot.Objects.Length == 0 || string.IsNullOrWhiteSpace(snapshot.Identity))
                throw new IOException("Incomplete or wrong-domain inventory.");
            Current = snapshot with { Id = Guid.NewGuid().ToString("D") };
            LastError = null;
            return Current;
        } catch (Exception ex) {
            LastError = L.T("directory.failed");
            logger.LogWarning(ex, "Read-only directory refresh failed");
            throw new DirectoryUnavailableException(LastError);
        } finally { gate.Release(); }
    }
}
public sealed class DirectoryUnavailableException(string message) : Exception(message);
public static class Json {
    public static readonly JsonSerializerOptions Settings = new(JsonSerializerDefaults.Web) {
        WriteIndented = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
}
