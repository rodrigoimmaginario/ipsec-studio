using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IpsecStudio;

public sealed record DeployRequest(string PlanId, int Version, string Hash, string Action, string? Approval = null) {
    // Drift (edited Studio GPOs or external IPsec GPOs) blocks apply/rollback unless the operator acknowledges it explicitly.
    public bool AcknowledgeDrift { get; init; }
}
public sealed record HostRequest(string PlanId, int Version, string Action, string[]? ComputerIds = null) {
    // Probe only: capture packets on the responders while probing (NDIS trace), keep counts, delete the capture.
    public bool Capture { get; init; }
}
public sealed record WaveRequest(string PlanId, int Version, string WaveId, string Action, string? Approval = null);
public sealed record DeploymentRecord(DateTimeOffset Utc, string Identity, string PlanId, int Version, string Hash, string Action, bool Succeeded, string Output) {
    public string? Approval { get; init; }
    public string? WaveId { get; init; }
    public bool? ExpansionAllowed { get; init; }
}
public sealed record WaveResult(string WaveId, string WaveName, bool Succeeded, bool ExpansionAllowed, string Output, HostResult[] Verify, ProbeResult[] Probe);
public sealed record HostResult(string Host, bool Reachable, string Output) {
    // Identity propagation: the machine TGT (logon session 0x3e7) carries the groups IPsec peers will see.
    public string IdentityState { get; init; } = "unknown";
    public DateTimeOffset? TgtStartUtc { get; init; }
    public DateTimeOffset? LastGroupChangeUtc { get; init; }
}
public sealed record ProbeResult(string Initiator, string Responder, string Expected, string Observed, bool Match, string Detail) {
    public WireObservation? Wire { get; init; }
}

// Executes compiled scripts under the identity of the user running IPsec Studio (like GPMC). No service account, no stored secrets.
public sealed class Deployment(IConfiguration config, PlanRepository repository, IScriptRunner runner, ILogger<Deployment> logger) {
    private readonly SemaphoreSlim gate = new(1, 1);
    public bool Enabled => config.GetValue("Deployment:Enabled", false);
    public int ProbePort => Math.Clamp(config.GetValue("Deployment:ProbePort", 5985), 1, 65535);
    public async Task<DeploymentRecord> Execute(DeployRequest request, string identity) {
        if (!Enabled) throw new UnauthorizedAccessException(L.T("deploy.disabled"));
        if (request.Action is not ("apply" or "rollback" or "groups")) throw new ValidationException(L.T("deploy.action"));
        if (request.Action is "apply" or "groups" && string.IsNullOrWhiteSpace(request.Approval)) throw new ValidationException(L.T("deploy.approval"));
        await gate.WaitAsync();
        try {
            var plan = await repository.Get(request.PlanId) ?? throw new ValidationException(L.T("deploy.planNotFound"));
            if (plan.Version != request.Version) throw new ConflictException(L.T("deploy.stale"));
            string script, hash;
            if (request.Action == "groups") { var g = Compiler.CompileGroups(plan); script = g.ApplyScript; hash = g.Sha256; }
            else { var c = Compiler.Compile(plan); script = request.Action == "apply" ? c.ApplyScript : c.RollbackScript; hash = c.Sha256; }
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(request.Hash ?? "")))
                throw new ConflictException(L.T("deploy.hash"));
            if (request.Action is "apply" or "rollback") {
                var drift = await ReadDrift(plan, request.Action == "apply");
                if (!drift.Clean) {
                    if (!request.AcknowledgeDrift) throw new ConflictException(L.T("drift.blocked", Describe(drift)));
                    await repository.AppendDeployment(new(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, hash, "drift-acknowledged", true, JsonSerializer.Serialize(drift, Json.Settings)) { Approval = request.Approval?.Trim() });
                }
            }
            var (ok, output) = await runner.Run(script, TimeSpan.FromMinutes(10));
            var record = new DeploymentRecord(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, hash, request.Action, ok, output) { Approval = request.Approval?.Trim() };
            await repository.AppendDeployment(record);
            return record;
        } finally { gate.Release(); }
    }
    public async Task<DriftReport> Drift(string planId, int version) {
        if (!Enabled) throw new UnauthorizedAccessException(L.T("deploy.disabled"));
        var plan = await repository.Get(planId) ?? throw new ValidationException(L.T("deploy.planNotFound"));
        if (plan.Version != version) throw new ConflictException(L.T("deploy.stale"));
        return await ReadDrift(plan, false);
    }
    // Before apply the Studio GPOs are expected to be absent: only external IPsec GPOs count. Before rollback/verify, every GPO must match.
    async Task<DriftReport> ReadDrift(SavedPlan plan, bool beforeApply) {
        var c = Compiler.Compile(plan);
        var spec = new { domain = plan.Inventory.Domain, target = plan.Input.Settings.LinkTarget == "domain" ? plan.Inventory.DomainDn : plan.Input.Settings.LinkTarget, gpos = c.Gpos.Select(g => g.Name) };
        var (ok, output) = await runner.Run(DriftScript.Replace("__DRIFT__", JsonSerializer.Serialize(spec)), TimeSpan.FromMinutes(5));
        if (!ok) throw new InvalidOperationException(L.T("deploy.remoteFailed", output));
        var reading = IpsecStudio.Drift.Parse(output);
        var report = IpsecStudio.Drift.Compare(c, reading);
        // Before apply, Studio GPOs must be absent (present ones are drift); before rollback, absent ones are simply nothing to remove.
        var relevant = beforeApply ? report.Gpos.Where(g => g.Exists).Select(g => g with { Differences = g.Differences.Length == 0 ? [L.T("drift.present")] : g.Differences }).ToArray()
                                   : report.Gpos.Where(g => g.Exists).ToArray();
        var absent = report.Gpos.Where(g => !g.Exists).Select(g => g with { Differences = beforeApply ? [] : g.Differences }).ToArray();
        var dirty = relevant.Any(g => g.Differences.Length > 0) || reading.External.Length > 0;
        return report with { Gpos = [.. relevant, .. absent], Clean = !dirty };
    }
    static string Describe(DriftReport r) => string.Join(" | ", r.Gpos.Where(g => g.Differences.Length > 0).Select(g => g.Name + ": " + string.Join("; ", g.Differences)).Concat(r.External.Select(e => L.T("drift.external", e.Name, e.RuleCount))));
    // A wave adds (or removes) members of profile groups, then refreshes, verifies and probes only the wave's hosts.
    // Expansion to the next wave is allowed only when the script, the verification and the probe all succeeded.
    public async Task<WaveResult> ApplyWave(WaveRequest request, string identity) {
        if (!Enabled) throw new UnauthorizedAccessException(L.T("deploy.disabled"));
        if (request.Action is not ("wave-apply" or "wave-rollback")) throw new ValidationException(L.T("deploy.action"));
        if (request.Action == "wave-apply" && string.IsNullOrWhiteSpace(request.Approval)) throw new ValidationException(L.T("deploy.approval"));
        await gate.WaitAsync();
        try {
            var plan = await repository.Get(request.PlanId) ?? throw new ValidationException(L.T("deploy.planNotFound"));
            if (plan.Version != request.Version) throw new ConflictException(L.T("deploy.stale"));
            var index = Array.FindIndex(plan.Input.Waves, w => w.Id == request.WaveId);
            if (index < 0) throw new ValidationException(L.T("waves.notFound"));
            var log = await repository.ListDeployments(plan.Id);
            if (request.Action == "wave-apply")
                foreach (var earlier in plan.Input.Waves.Take(index)) {
                    var last = log.Where(r => r.WaveId == earlier.Id).LastOrDefault();
                    if (last is { Action: "wave-apply", ExpansionAllowed: false }) throw new ConflictException(L.T("waves.blocked", earlier.Name));
                }
            var wave = Compiler.CompileWave(plan, request.WaveId);
            var apply = request.Action == "wave-apply";
            var (ok, output) = await runner.Run(apply ? wave.ApplyScript : wave.RollbackScript, TimeSpan.FromMinutes(5));
            HostResult[] verify = []; ProbeResult[] probe = []; var expansion = ok;
            if (ok) {
                var membership = PlanRules.AfterWaves(plan.Input, plan.Inventory, apply ? index + 1 : index);
                await RunHosts(plan, wave.ComputerIds, "refresh", identity);
                verify = await RunHosts(plan, wave.ComputerIds, "verify", identity);
                probe = await RunProbe(plan, membership, wave.ComputerIds, identity);
                expansion = verify.All(h => h.Reachable && IsHealthy(h.Output)) && probe.All(p => p.Match);
            }
            await repository.AppendDeployment(new(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, wave.Sha256, request.Action, ok, output) { Approval = request.Approval?.Trim(), WaveId = wave.WaveId, ExpansionAllowed = apply ? expansion : null });
            return new(wave.WaveId, wave.Name, ok, apply && expansion, output, verify, probe);
        } finally { gate.Release(); }
    }
    static bool IsHealthy(string output) { try { using var doc = JsonDocument.Parse(output); return doc.RootElement.TryGetProperty("healthy", out var h) && h.GetBoolean(); } catch { return false; } }
    // Membership as the AD should look now: inventory plus every wave whose last record is a successful apply.
    async Task<InventorySnapshot> AppliedMembership(SavedPlan plan) {
        var log = await repository.ListDeployments(plan.Id);
        var count = 0;
        foreach (var w in plan.Input.Waves) { var last = log.Where(r => r.WaveId == w.Id).LastOrDefault(); if (last is { Action: "wave-apply", Succeeded: true }) count++; else break; }
        return PlanRules.AfterWaves(plan.Input, plan.Inventory, count);
    }
    public async Task<HostResult[]> HostOperation(HostRequest request, string identity) {
        if (!Enabled) throw new UnauthorizedAccessException(L.T("deploy.remoteDisabled"));
        if (request.Action is not ("refresh" or "verify")) throw new ValidationException(L.T("deploy.action"));
        var plan = await repository.Get(request.PlanId) ?? throw new ValidationException(L.T("deploy.planNotFound"));
        if (plan.Version != request.Version) throw new ConflictException(L.T("deploy.stale"));
        return await RunHosts(plan, request.ComputerIds, request.Action, identity);
    }
    async Task<HostResult[]> RunHosts(SavedPlan plan, string[]? computerIds, string action, string identity) {
        var hosts = TargetHosts(plan, computerIds);
        var script = (action == "refresh" ? RefreshScript : VerifyScript).Replace("__HOSTS__", JsonSerializer.Serialize(hosts.Select(h => h.Name + "." + plan.Inventory.Domain)))
            .Replace("__GROUPSIDS__", JsonSerializer.Serialize(ProfileGroupSids(plan)));
        var (ok, output) = await runner.Run(script, TimeSpan.FromMinutes(6));
        await repository.AppendDeployment(new(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, "", action, ok, output));
        if (!ok) throw new InvalidOperationException(L.T("deploy.remoteFailed", output));
        var results = JsonSerializer.Deserialize<HostResult[]>(output, Json.Settings) ?? [];
        return action == "verify" ? results.Select(WithIdentity).ToArray() : results;
    }
    static string[] ProfileGroupSids(SavedPlan plan) {
        var map = plan.Inventory.Objects.ToDictionary(o => o.Id);
        var ids = new List<string?> { plan.Input.BaseGroupId, plan.Input.NoPolicyGroupId, plan.Input.BoundaryGroupId, plan.Input.ExclusionGroupId, plan.Input.Settings.ManagementGroupId };
        ids.AddRange(plan.Input.Restricted.Where(r => r.Included).SelectMany(r => new[] { r.ProtectedGroupId, r.AccessGroupId }));
        var dc = PlanRules.DomainComputers(plan.Inventory); if (plan.Input.BaseGroupId is null && dc is not null) ids.Add(dc.Id);
        return ids.Where(id => id is not null && map.TryGetValue(id, out var g) && PlanRules.ValidSid(g.Sid)).Select(id => map[id!].Sid!).Distinct().ToArray();
    }
    static HostResult WithIdentity(HostResult h) {
        if (!h.Reachable) return h;
        try {
            using var doc = JsonDocument.Parse(h.Output);
            DateTimeOffset? Read(string name) => doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(v.GetString(), null, System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d.ToUniversalTime() : null;
            var tgt = Read("tgtStartUtc"); var change = Read("lastGroupChangeUtc");
            return h with { IdentityState = IdentityState(tgt, change), TgtStartUtc = tgt, LastGroupChangeUtc = change };
        } catch (JsonException) { return h; }
    }
    public static string IdentityState(DateTimeOffset? tgtStart, DateTimeOffset? lastGroupChange) =>
        tgtStart is null || lastGroupChange is null ? "unknown" : tgtStart >= lastGroupChange ? "synced" : "pending";
    public async Task<ProbeResult[]> Probe(HostRequest request, string identity) {
        if (!Enabled) throw new UnauthorizedAccessException(L.T("deploy.probeDisabled"));
        var plan = await repository.Get(request.PlanId) ?? throw new ValidationException(L.T("deploy.planNotFound"));
        if (plan.Version != request.Version) throw new ConflictException(L.T("deploy.stale"));
        return await RunProbe(plan, await AppliedMembership(plan), request.ComputerIds, identity, request.Capture);
    }
    async Task<ProbeResult[]> RunProbe(SavedPlan plan, InventorySnapshot membership, string[]? computerIds, string identity, bool capture = false) {
        var a = PlanRules.Assess(plan.Input, membership);
        var initiators = TargetHosts(plan, computerIds);
        var responders = a.Hosts.Where(h => h.Profile != "dc" && h.Ip is not null).Select(h => new { h.ComputerId, h.Name, Ip = h.Ip!, Port = ProbePort })
            .Concat(plan.Input.ExternalHosts.Select(x => new { ComputerId = x.Id, x.Name, Ip = x.Address, Port = ProbePort })).ToArray();
        var studioIps = System.Net.Dns.GetHostAddresses(Environment.MachineName).Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Select(ip => ip.ToString()).ToArray();
        var spec = new { initiators = initiators.Select(h => new { h.Name, Fqdn = h.Name + "." + plan.Inventory.Domain, h.ComputerId }), responders, studioIps };
        // Capture on domain responders other than the Studio host itself; hosts outside the domain are never captured.
        var captureHosts = capture ? a.Hosts.Where(h => h.Profile != "dc" && h.Ip is not null && !h.Name.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) && System.Text.RegularExpressions.Regex.IsMatch(h.Name, @"\A[A-Za-z0-9-]{1,63}\z"))
            .Select(h => new { name = h.Name, fqdn = h.Name + "." + plan.Inventory.Domain }).ToArray() : [];
        var started = Array.Empty<string>();
        if (captureHosts.Length > 0) {
            var (sok, sout) = await runner.Run(CaptureStartScript.Replace("__HOSTS__", JsonSerializer.Serialize(captureHosts)), TimeSpan.FromMinutes(5));
            await repository.AppendDeployment(new(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, "", "capture-start", sok, sout));
            if (sok) started = (JsonSerializer.Deserialize<CaptureHost[]>(sout, Json.Settings) ?? []).Where(h => h.Ok).Select(h => h.Host).ToArray();
        }
        string output; CaptureHost[] captured = [];
        try {
            var (ok, o) = await runner.Run(ProbeScript.Replace("__SPEC__", JsonSerializer.Serialize(spec)), TimeSpan.FromMinutes(10));
            output = o;
            await repository.AppendDeployment(new(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, "", "probe", ok, output));
            if (!ok) throw new InvalidOperationException(L.T("deploy.probeFailed", output));
        } finally {
            if (started.Length > 0) {
                var stopSpec = new { hosts = captureHosts.Where(h => started.Contains(h.name)), port = ProbePort };
                var (tok, tout) = await runner.Run(CaptureStopScript.Replace("__SPEC__", JsonSerializer.Serialize(stopSpec)), TimeSpan.FromMinutes(10));
                await repository.AppendDeployment(new(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, "", "capture-stop", tok, tout));
                if (tok) captured = (JsonSerializer.Deserialize<CaptureHost[]>(tout, Json.Settings) ?? []).Where(h => h.Ok).ToArray();
            }
        }
        var ips = a.Hosts.Where(h => h.Ip is not null).ToDictionary(h => h.ComputerId, h => h.Ip!);
        var raw = JsonSerializer.Deserialize<RawProbe[]>(output, Json.Settings) ?? [];
        return raw.Select(p => {
            var expected = p.Error is not null ? "indeterminate" : Simulator.Evaluate(plan.Input, membership, a, p.InitiatorId, p.ResponderId).Outcome;
            var observed = Classify(p.Tcp, p.Cipher, p.Error);
            var result = new ProbeResult(p.Initiator, p.Responder, expected, observed, expected == observed, p.Error ?? $"tcp={p.Tcp} cipher={p.Cipher ?? "-"} integrity={p.Integrity ?? "-"} auth={p.Auth ?? "-"}");
            var host = captured.FirstOrDefault(h => h.Host.Equals(p.Responder, StringComparison.OrdinalIgnoreCase));
            if (host is null || !ips.TryGetValue(p.InitiatorId, out var from) || !ips.TryGetValue(p.ResponderId, out var to)) return result;
            var (esp, clear, attempts) = Wire.Count(host.Buckets ?? [], from, to, ProbePort);
            return result with { Wire = new(esp, clear, attempts, Wire.Judge(expected, esp, clear)) };
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
    // Reads Studio GPOs and any other GPO with connection security rules. Group names come from GPO permissions (display only).
    const string DriftScript = """
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$spec=ConvertFrom-Json @'
__DRIFT__
'@
Import-Module GroupPolicy; Import-Module ActiveDirectory
$domain=Get-ADDomain
if($domain.DNSRoot -ne $spec.domain){throw "Wrong domain: $($domain.DNSRoot)"}
$apply=[guid]'edacfd8f-ffb3-11d1-b41d-00a0c968f939'
$links=@{}
foreach($l in (Get-GPInheritance -Target $spec.target).GpoLinks){ $links[$l.DisplayName]=[bool]$l.Enabled }
$gpos=@()
foreach($name in @($spec.gpos)){
  $g=Get-GPO -Name $name -ErrorAction SilentlyContinue
  if(-not $g){ $gpos+=@{name=$name;exists=$false;marker=$null;version=0;linked=$false;rules=@();authorizationSddl='';exemptions='';applyGroups=@();denySids=@()}; continue }
  $store=$spec.domain+'\'+$name
  $errors=@(); $rules=@(); $setting=$null
  try{ $rules=@(Get-NetIPsecRule -PolicyStore $store -ErrorAction Stop | ForEach-Object { $af=$_|Get-NetFirewallAddressFilter -ErrorAction Stop; @{name=$_.IPsecRuleName;inbound=$_.InboundSecurity.ToString();outbound=$_.OutboundSecurity.ToString();requireAuthorization=[bool]$_.RequireAuthorization;remoteAddress=(@($af.RemoteAddress) -join ',')} }) }catch{ $errors+="rules: $($_.Exception.Message)" }
  try{ $setting=Get-NetFirewallSetting -PolicyStore $store -ErrorAction Stop }catch{ $errors+="setting: $($_.Exception.Message)" }
  $crypto=$null
  try{
    $mmSets=@(Get-NetIPsecMainModeCryptoSet -PolicyStore $store -ErrorAction Stop)
    $qmSets=@(Get-NetIPsecQuickModeCryptoSet -PolicyStore $store -ErrorAction Stop)
    $p1Sets=@(Get-NetIPsecPhase1AuthSet -PolicyStore $store -ErrorAction Stop)
    $crypto=@{mainMode=@($mmSets | ForEach-Object { $_.Proposal } | ForEach-Object { "$($_.Encryption)-$($_.Hash)-$($_.KeyExchange)" });mainModeMinutes=$(if($mmSets.Count){[int]$mmSets[0].MaxMinutes}else{0});quickMode=@($qmSets | ForEach-Object { $_.Proposal } | ForEach-Object { "$($_.Encapsulation)-$($_.Encryption)-$($_.ESPHash)-$($_.MaxMinutes)" });phase1=@($p1Sets | ForEach-Object { $_.Proposal } | ForEach-Object { "$($_.AuthenticationMethod)" })}
  }catch{ $errors+="crypto: $($_.Exception.Message)" }
  $perm=@(Get-GPPermission -Guid $g.Id -All | Where-Object { $_.Permission -eq 'GpoApply' } | ForEach-Object { $_.Trustee.Name })
  $acl=Get-Acl -Path ('AD:\'+$g.Path)
  $deny=@($acl.Access | Where-Object { $_.AccessControlType -eq 'Deny' -and $_.ObjectType -eq $apply } | ForEach-Object { $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value })
  $gpos+=@{name=$name;exists=$true;marker=$g.Description;version=[int]$g.Computer.DSVersion;linked=[bool]$links[$name];rules=$rules;authorizationSddl=[string]$setting.RemoteMachineTransportAuthorizationList;exemptions=[string]$setting.Exemptions;applyGroups=$perm;denySids=$deny;errors=$errors;crypto=$crypto}
}
$external=@()
foreach($g in (Get-GPO -All | Where-Object { $_.DisplayName -notin @($spec.gpos) })){
  $n=@(Get-NetIPsecRule -PolicyStore ($spec.domain+'\'+$g.DisplayName) -ErrorAction Stop).Count
  if($n -gt 0){ $report=[xml](Get-GPOReport -Guid $g.Id -ReportType Xml); $external+=@{name=$g.DisplayName;ruleCount=$n;links=@($report.GPO.LinksTo | ForEach-Object { $_.SOMPath })} }
}
@{gpos=$gpos;external=$external} | ConvertTo-Json -Compress -Depth 6
""";
    const string RemotePreamble = """
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$hosts=ConvertFrom-Json @'
__HOSTS__
'@
$groupSids=ConvertFrom-Json @'
__GROUPSIDS__
'@
# Last change of any profile group (membership edits update whenChanged): hosts whose machine TGT predates it may still carry old groups.
$lastChange=$null
try{ Import-Module ActiveDirectory -ErrorAction Stop; $times=@(foreach($sid in @($groupSids)){ (Get-ADGroup -Identity $sid -Properties whenChanged).whenChanged }); if($times.Count){ $lastChange=($times | Sort-Object | Select-Object -Last 1).ToUniversalTime().ToString('o') } }catch{}
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
    $out=Invoke-Command -ComputerName $h -ArgumentList $lastChange -ScriptBlock {
      param($lastChangeUtc)
      $tgt=$null
      try{ $text=(klist.exe -li 0x3e7 tgt 2>&1 | Out-String);  if($text -match 'Start\s*Time\s*:\s*([^\r\n]+?)\s*\(local\)'){ $tgt=[datetime]::Parse($matches[1],[Globalization.CultureInfo]::CurrentCulture).ToUniversalTime().ToString('o') } }catch{}
      $rules=@(Get-NetIPsecRule -PolicyStore ActiveStore -TracePolicyStore | Where-Object DisplayName -like 'IpsecStudio*' | ForEach-Object {@{name=$_.IPsecRuleName;display=$_.DisplayName;inbound=$_.InboundSecurity.ToString();outbound=$_.OutboundSecurity.ToString();requireAuthorization=[bool]$_.RequireAuthorization;source=$_.PolicyStoreSource;enabled=$_.Enabled.ToString();status=$_.PrimaryStatus.ToString();statusText=[string]$_.Status}})
      $setting=Get-NetFirewallSetting -PolicyStore ActiveStore
      $all=@(Get-NetIPsecRule -PolicyStore ActiveStore | Where-Object Enabled -eq True).Count
      @{computer=$env:COMPUTERNAME;studioRules=$rules;enabledIpsecRules=$all;authorizationList=[string]$setting.RemoteMachineTransportAuthorizationList;exemptions=[string]$setting.Exemptions;gpos=@($rules | ForEach-Object {$_.source} | Sort-Object -Unique);healthy=(@($rules | Where-Object {$_.status -ne 'OK'}).Count -eq 0 -and $rules.Count -gt 0);tgtStartUtc=$tgt;lastGroupChangeUtc=$lastChangeUtc}
    }
""" + NL + RemoteEpilogue;
    // Starts an NDIS packet capture on each responder. Refuses a host where another trace is already running.
    const string CaptureStartScript = """
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$hosts=ConvertFrom-Json @'
__HOSTS__
'@
$results=@()
foreach($h in @($hosts)){
  try{
    [void](Invoke-Command -ComputerName $h.fqdn -ScriptBlock {
      $dir='C:\ProgramData\IpsecStudio\capture'; New-Item -ItemType Directory -Path $dir -Force | Out-Null
      $status=(netsh.exe trace show status) -join ' '
      if($status -notmatch 'no trace session'){ throw "another trace session is running" }
      Remove-Item (Join-Path $dir 'probe.*') -Force -ErrorAction SilentlyContinue
      $out=netsh.exe trace start capture=yes report=no persistent=no maxsize=128 overwrite=yes tracefile=(Join-Path $dir 'probe.etl')
      if($LASTEXITCODE -ne 0){ throw "netsh trace start failed: $($out -join ' ')" }
    })
    $results+=@{host=$h.name;ok=$true;error=$null;bytes=0;sha256=$null;buckets=@()}
  }catch{ $results+=@{host=$h.name;ok=$false;error=$_.Exception.Message;bytes=0;sha256=$null;buckets=@()} }
}
ConvertTo-Json @($results) -Compress -Depth 4
""";
    // Stops the capture, decodes it with pktmon, buckets IPv4 packets (source, destination, port, esp/tcp) and deletes the files.
    const string CaptureStopScript = """
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$spec=ConvertFrom-Json @'
__SPEC__
'@
$results=@()
foreach($h in @($spec.hosts)){
  try{
    $r=Invoke-Command -ComputerName $h.fqdn -ScriptBlock {
      $dir='C:\ProgramData\IpsecStudio\capture'; $etl=Join-Path $dir 'probe.etl'; $txt=Join-Path $dir 'probe.txt'
      try{
        netsh.exe trace stop | Out-Null
        if(-not (Test-Path $etl)){ throw 'capture file missing' }
        $bytes=(Get-Item $etl).Length; $sha=(Get-FileHash $etl -Algorithm SHA256).Hash.ToLowerInvariant()
        pktmon.exe etl2txt $etl --out $txt | Out-Null
        if($LASTEXITCODE -ne 0){ throw 'pktmon etl2txt failed' }
        $b=@{}
        foreach($line in [IO.File]::ReadLines($txt)){
          $m=[regex]::Match($line,'length \d+: (\d+\.\d+\.\d+\.\d+)(?:\.(\d+))? > (\d+\.\d+\.\d+\.\d+)(?:\.(\d+))?: (.*)$')
          if(-not $m.Success){ continue }
          $rest=$m.Groups[5].Value
          $kind=$null; $port=0
          if($rest -match '^(ESP|ip-proto-50)'){ $kind='esp' }
          elseif($rest -match '^Flags \[([^\]]*)\]'){
            $fl=$matches[1]
            # syn = clear connection attempt (port = destination); synack = responder accepting in clear (port = source); tcp = other clear segment (port = destination).
            if($fl -match 'S' -and $fl -notmatch '\.'){ $kind='syn'; $port=[int]$m.Groups[4].Value }
            elseif($fl -match 'S'){ $kind='synack'; $port=[int]$m.Groups[2].Value }
            else{ $kind='tcp'; $port=[int]$m.Groups[4].Value }
          }
          if(-not $kind){ continue }
          $key=$m.Groups[1].Value+'|'+$m.Groups[3].Value+'|'+$port+'|'+$kind
          $b[$key]=1+[int]$b[$key]
        }
        $buckets=@($b.GetEnumerator() | ForEach-Object { $p=$_.Key -split '\|'; @{source=$p[0];destination=$p[1];port=[int]$p[2];kind=$p[3];count=[int]$_.Value} })
        @{ok=$true;error=$null;bytes=$bytes;sha256=$sha;buckets=$buckets}
      }catch{ @{ok=$false;error=$_.Exception.Message;bytes=0;sha256=$null;buckets=@()} }
      finally{ Remove-Item $etl,$txt -Force -ErrorAction SilentlyContinue; Remove-Item (Join-Path $dir 'probe*.cab') -Force -ErrorAction SilentlyContinue }
    }
    $results+=@{host=$h.name;ok=[bool]$r.ok;error=$r.error;bytes=[long]$r.bytes;sha256=$r.sha256;buckets=@($r.buckets)}
  }catch{ $results+=@{host=$h.name;ok=$false;error=$_.Exception.Message;bytes=0;sha256=$null;buckets=@()} }
}
ConvertTo-Json @($results) -Compress -Depth 6
""";
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
