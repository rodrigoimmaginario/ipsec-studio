using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IpsecStudio;

public sealed record DeployRequest(string PlanId, int Version, string Hash, string Action);
public sealed record HostRequest(string PlanId, int Version, string Action, string[]? ComputerIds = null);
public sealed record DeploymentRecord(DateTimeOffset Utc, string Identity, string PlanId, int Version, string Hash, string Action, bool Succeeded, string Output);
public sealed record HostResult(string Host, bool Reachable, string Output);
public sealed record ProbeResult(string Initiator, string Responder, string Expected, string Observed, bool Match, string Detail);

// Executes compiled scripts under the identity of the user running IPsec Studio (like GPMC). No service account, no stored secrets.
public sealed class Deployment(IConfiguration config, PlanRepository repository, ILogger<Deployment> logger) {
    private readonly SemaphoreSlim gate = new(1, 1);
    public bool Enabled => config.GetValue("Deployment:Enabled", false);
    public int ProbePort => Math.Clamp(config.GetValue("Deployment:ProbePort", 5985), 1, 65535);
    public async Task<DeploymentRecord> Execute(DeployRequest request, string identity) {
        if (!Enabled) throw new UnauthorizedAccessException(L.T("deploy.disabled"));
        if (request.Action is not ("apply" or "rollback")) throw new ValidationException(L.T("deploy.action"));
        await gate.WaitAsync();
        try {
            var plan = await repository.Get(request.PlanId) ?? throw new ValidationException(L.T("deploy.planNotFound"));
            if (plan.Version != request.Version) throw new ConflictException(L.T("deploy.stale"));
            var compilation = Compiler.Compile(plan);
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(compilation.Sha256), Encoding.ASCII.GetBytes(request.Hash ?? "")))
                throw new ConflictException(L.T("deploy.hash"));
            var (ok, output) = await RunPowerShell(request.Action == "apply" ? compilation.ApplyScript : compilation.RollbackScript, TimeSpan.FromMinutes(10));
            var record = new DeploymentRecord(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, compilation.Sha256, request.Action, ok, output);
            await repository.AppendDeployment(record);
            return record;
        } finally { gate.Release(); }
    }
    public async Task<HostResult[]> HostOperation(HostRequest request, string identity) {
        if (!Enabled) throw new UnauthorizedAccessException(L.T("deploy.remoteDisabled"));
        if (request.Action is not ("refresh" or "verify")) throw new ValidationException(L.T("deploy.action"));
        var plan = await repository.Get(request.PlanId) ?? throw new ValidationException(L.T("deploy.planNotFound"));
        if (plan.Version != request.Version) throw new ConflictException(L.T("deploy.stale"));
        var hosts = TargetHosts(plan, request.ComputerIds);
        var script = (request.Action == "refresh" ? RefreshScript : VerifyScript).Replace("__HOSTS__", JsonSerializer.Serialize(hosts.Select(h => h.Name + "." + plan.Inventory.Domain)));
        var (ok, output) = await RunPowerShell(script, TimeSpan.FromMinutes(6));
        await repository.AppendDeployment(new(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, "", request.Action, ok, output));
        if (!ok) throw new InvalidOperationException(L.T("deploy.remoteFailed", output));
        return JsonSerializer.Deserialize<HostResult[]>(output, Json.Settings) ?? [];
    }
    public async Task<ProbeResult[]> Probe(HostRequest request, string identity) {
        if (!Enabled) throw new UnauthorizedAccessException(L.T("deploy.probeDisabled"));
        var plan = await repository.Get(request.PlanId) ?? throw new ValidationException(L.T("deploy.planNotFound"));
        if (plan.Version != request.Version) throw new ConflictException(L.T("deploy.stale"));
        var a = PlanRules.Assess(plan.Input, plan.Inventory);
        var initiators = TargetHosts(plan, request.ComputerIds);
        var responders = a.Hosts.Where(h => h.Profile != "dc" && h.Ip is not null).Select(h => new { h.ComputerId, h.Name, Ip = h.Ip!, Port = ProbePort })
            .Concat(plan.Input.ExternalHosts.Select(x => new { ComputerId = x.Id, x.Name, Ip = x.Address, Port = ProbePort })).ToArray();
        var studioIps = System.Net.Dns.GetHostAddresses(Environment.MachineName).Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Select(ip => ip.ToString()).ToArray();
        var spec = new { initiators = initiators.Select(h => new { h.Name, Fqdn = h.Name + "." + plan.Inventory.Domain, h.ComputerId }), responders, studioIps };
        var (ok, output) = await RunPowerShell(ProbeScript.Replace("__SPEC__", JsonSerializer.Serialize(spec)), TimeSpan.FromMinutes(10));
        await repository.AppendDeployment(new(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, "", "probe", ok, output));
        if (!ok) throw new InvalidOperationException(L.T("deploy.probeFailed", output));
        var raw = JsonSerializer.Deserialize<RawProbe[]>(output, Json.Settings) ?? [];
        return raw.Select(p => {
            var expected = p.Error is not null ? "indeterminate" : Simulator.Evaluate(plan.Input, plan.Inventory, a, p.InitiatorId, p.ResponderId).Outcome;
            var observed = Classify(p.Tcp, p.Cipher, p.Error);
            return new ProbeResult(p.Initiator, p.Responder, expected, observed, expected == observed, p.Error ?? $"tcp={p.Tcp} cipher={p.Cipher ?? "-"} integrity={p.Integrity ?? "-"} auth={p.Auth ?? "-"}");
        }).ToArray();
    }
    // Observed outcome from one probe: TCP result plus the quick mode SA read on the initiator.
    public static string Classify(bool tcp, string? cipher, string? error) => error is not null ? "unreachable" : !tcp ? "blocked" : cipher is null ? "clear" : cipher == "None" ? "authenticated" : "encrypted";
    sealed record RawProbe(string Initiator, string InitiatorId, string Responder, string ResponderId, bool Tcp, string? Cipher, string? Integrity, string? Auth, string? Error);
    static HostProfile[] TargetHosts(SavedPlan plan, string[]? ids) {
        var a = PlanRules.Assess(plan.Input, plan.Inventory);
        var hosts = a.Hosts.Where(h => h.Profile != "dc" && h.Profile != "none");
        if (ids is { Length: > 0 }) hosts = hosts.Where(h => ids.Contains(h.ComputerId));
        var result = hosts.ToArray();
        if (result.Length == 0) throw new ValidationException(L.T("deploy.noHosts"));
        foreach (var h in result) if (!System.Text.RegularExpressions.Regex.IsMatch(h.Name, @"\A[A-Za-z0-9-]{1,63}\z")) throw new ValidationException(L.T("deploy.badHostName", h.Name));
        return result;
    }
    async Task<(bool Ok, string Output)> RunPowerShell(string script, TimeSpan timeout) {
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
    const string RemotePreamble = """
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$hosts=ConvertFrom-Json @'
__HOSTS__
'@
$results=@()
foreach($h in @($hosts)){
  try{
""";
    const string RemoteEpilogue = """
    $results+=@{host=$h;reachable=$true;output=($out|ConvertTo-Json -Compress -Depth 6)}
  }catch{$results+=@{host=$h;reachable=$false;output=$_.Exception.Message}}
}
ConvertTo-Json @($results) -Compress -Depth 8
""";
    const string NL = "\n";
    const string RefreshScript = RemotePreamble + NL + """
    $out=Invoke-Command -ComputerName $h -ScriptBlock {
      klist.exe -li 0x3e7 purge | Out-Null
      gpupdate.exe /target:computer /force /wait:120 | Out-Null
      Get-NetIPsecMainModeSA -ErrorAction SilentlyContinue | Remove-NetIPsecMainModeSA -ErrorAction SilentlyContinue
      Get-NetIPsecQuickModeSA -ErrorAction SilentlyContinue | Remove-NetIPsecQuickModeSA -ErrorAction SilentlyContinue
      @{computer=$env:COMPUTERNAME;refreshedUtc=(Get-Date).ToUniversalTime().ToString('o');gpupdateExit=$LASTEXITCODE}
    }
""" + NL + RemoteEpilogue;
    const string VerifyScript = RemotePreamble + NL + """
    $out=Invoke-Command -ComputerName $h -ScriptBlock {
      $rules=@(Get-NetIPsecRule -PolicyStore ActiveStore -TracePolicyStore | Where-Object DisplayName -like 'IpsecStudio*' | ForEach-Object {@{name=$_.IPsecRuleName;display=$_.DisplayName;inbound=$_.InboundSecurity.ToString();outbound=$_.OutboundSecurity.ToString();requireAuthorization=[bool]$_.RequireAuthorization;source=$_.PolicyStoreSource;enabled=$_.Enabled.ToString();status=$_.PrimaryStatus.ToString();statusText=[string]$_.Status}})
      $setting=Get-NetFirewallSetting -PolicyStore ActiveStore
      $all=@(Get-NetIPsecRule -PolicyStore ActiveStore | Where-Object Enabled -eq True).Count
      @{computer=$env:COMPUTERNAME;studioRules=$rules;enabledIpsecRules=$all;authorizationList=[string]$setting.RemoteMachineTransportAuthorizationList;exemptions=[string]$setting.Exemptions;gpos=@($rules | ForEach-Object {$_.source} | Sort-Object -Unique);healthy=(@($rules | Where-Object {$_.status -ne 'OK'}).Count -eq 0 -and $rules.Count -gt 0)}
    }
""" + NL + RemoteEpilogue;
    const string ProbeScript = """
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$spec=ConvertFrom-Json @'
__SPEC__
'@
$results=@()
foreach($i in @($spec.initiators)){
  try{
    $out=Invoke-Command -ComputerName $i.fqdn -ArgumentList @($spec.responders),@($spec.studioIps) -ScriptBlock {
      param($responders,$studioIps)
      # Fresh negotiations only: SAs are bidirectional and would hide the responder-side authorization decision.
      Get-NetIPsecMainModeSA -ErrorAction SilentlyContinue | Where-Object {$_.RemoteEndpoint -notin $studioIps} | Remove-NetIPsecMainModeSA -ErrorAction SilentlyContinue
      Get-NetIPsecQuickModeSA -ErrorAction SilentlyContinue | Where-Object {$_.RemoteEndpoint -notin $studioIps} | Remove-NetIPsecQuickModeSA -ErrorAction SilentlyContinue
      Start-Sleep -Milliseconds 500
      $r=@()
      foreach($t in @($responders)){
        if($t.ip -in @((Get-NetIPAddress -AddressFamily IPv4).IPAddress)){continue}
        $client=New-Object System.Net.Sockets.TcpClient
        $tcp=$false
        try{$async=$client.BeginConnect($t.ip,[int]$t.port,$null,$null);if($async.AsyncWaitHandle.WaitOne(6000) -and $client.Connected){$tcp=$true;$client.EndConnect($async)}}catch{}finally{$client.Close()}
        Start-Sleep -Milliseconds 300
        $qm=@(Get-NetIPsecQuickModeSA -ErrorAction SilentlyContinue | Where-Object RemoteEndpoint -eq $t.ip | Select-Object -First 1)
        $mm=@(Get-NetIPsecMainModeSA -ErrorAction SilentlyContinue | Where-Object RemoteEndpoint -eq $t.ip | Select-Object -First 1)
        $r+=@{responder=$t.name;responderId=$t.computerId;tcp=$tcp;cipher=$(if($qm.Count){$qm[0].FirstCipherAlgorithm.ToString()}else{$null});integrity=$(if($qm.Count){$qm[0].FirstIntegrityAlgorithm.ToString()}else{$null});auth=$(if($mm.Count){$mm[0].LocalFirstId.AuthenticationMethod.ToString()}else{$null})}
      }
      ,$r
    }
    foreach($x in @($out)){$results+=@{initiator=$i.name;initiatorId=$i.computerId;responder=$x.responder;responderId=$x.responderId;tcp=[bool]$x.tcp;cipher=$x.cipher;integrity=$x.integrity;auth=$x.auth;error=$null}}
  }catch{foreach($t in @($spec.responders)){$results+=@{initiator=$i.name;initiatorId=$i.computerId;responder=$t.name;responderId=$t.computerId;tcp=$false;cipher=$null;integrity=$null;auth=$null;error=$_.Exception.Message}}}
}
ConvertTo-Json @($results) -Compress -Depth 6
""";
}
