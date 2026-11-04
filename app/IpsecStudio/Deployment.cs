using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IpsecStudio;

public sealed record DeployRequest(string PlanId, int Version, string Hash, string Action, string? Approval = null) {
    // Drift (edited Studio GPOs or external IPsec GPOs) blocks apply/rollback unless the operator acknowledges it explicitly.
    public bool AcknowledgeDrift { get; init; }
    public bool AcknowledgeScope { get; init; }
}
public sealed record HostRequest(string PlanId, int Version, string Action, string[]? ComputerIds = null) {
    // Probe only: capture packets on the responders while probing (NDIS trace), keep counts, delete the capture.
    public bool Capture { get; init; }
}
public sealed record WaveRequest(string PlanId, int Version, string WaveId, string Action, string? Approval = null);
public sealed record DeploymentRecord(DateTimeOffset Utc, string Identity, string PlanId, int Version, string Hash, string Action, bool Succeeded, string Output) {
    public string? Approval { get; init; }
    public bool InProgress { get; init; }
    public string? WaveId { get; init; }
    public string[] Blockers { get; init; } = [];
    public bool? ExpansionAllowed { get; init; }
    // DCs that did not confirm the change within the replication wait ("timeout"/"unreachable"): hosts using them may keep old policy.
    public string[] PendingDcs { get; init; } = [];
}
public sealed record WaveResult(string WaveId, string WaveName, bool Succeeded, bool ExpansionAllowed, string Output, HostResult[] Verify, ProbeResult[] Probe) {
    public string[] Blockers { get; init; } = [];
    public string[] PendingDcs { get; init; } = [];
}
public sealed record HostResult(string Host, bool Reachable, string Output) {
    // Identity propagation: the machine TGT (logon session 0x3e7) carries the groups IPsec peers will see.
    public string IdentityState { get; init; } = "unknown";
    public DateTimeOffset? TgtStartUtc { get; init; }
    public DateTimeOffset? LastGroupChangeUtc { get; init; }
}
public sealed record ProbeResult(string Initiator, string Responder, string Expected, string Observed, bool Match, string Detail) {
    public WireObservation? Wire { get; init; }
    public bool Conclusive { get; init; } = true;
}

// Executes compiled scripts under the identity of the user running IPsec Studio (like GPMC). No service account, no stored secrets.
public sealed class Deployment(IConfiguration config, PlanRepository repository, IScriptRunner runner, ILogger<Deployment> logger) {
    private readonly SemaphoreSlim gate = new(1, 1);
    public bool Enabled => config.GetValue("Deployment:Enabled", false);
    public int ProbePort => Math.Clamp(config.GetValue("Deployment:ProbePort", 5985), 1, 65535);
    // Optional "IPv4:port" outside the secure subnet that every verified host must reach (lab: Azure WireServer 168.63.129.16:80).
    public string OutsideCheck {
        get {
            var v = (config["Deployment:OutsideCheck"] ?? "").Trim();
            if (v.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(v, @"\A(\d{1,3}\.){3}\d{1,3}:\d{1,5}\z")) throw new ValidationException(L.T("deploy.outsideInvalid"));
            return v;
        }
    }
    // After apply the Studio host is refreshed first (it needs the new policy to reach hosts that already require IPsec);
    // after rollback it goes last (it keeps the policy the others still require until they are refreshed).
    static string[] StudioIps() => System.Net.Dns.GetHostAddresses(Environment.MachineName).Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Select(ip => ip.ToString()).ToArray();
    // Hosts are refreshed/verified one after another (detached gpupdate, up to 5 min with a DC down): 2.5 minutes per host, never under 6.
    public static TimeSpan HostsTimeout(int hosts) => TimeSpan.FromMinutes(Math.Max(6, Math.Ceiling(2.5 * hosts)));
    public static IEnumerable<T> OrderStudio<T>(IEnumerable<T> hosts, Func<T, string> name, string machine, bool last) =>
        hosts.OrderBy(h => name(h).Equals(machine, StringComparison.OrdinalIgnoreCase) == last ? 1 : 0);
    public async Task<DeploymentRecord> Execute(DeployRequest request, string identity) {
        if (!Enabled) throw new UnauthorizedAccessException(L.T("deploy.disabled"));
        if (request.Action is not ("apply" or "rollback" or "groups")) throw new ValidationException(L.T("deploy.action"));
        if (request.Action is "apply" or "groups" && string.IsNullOrWhiteSpace(request.Approval)) throw new ValidationException(L.T("deploy.approval"));
        await gate.WaitAsync();
        try {
            var plan = await repository.Get(request.PlanId, request.Action == "rollback" ? request.Version : null) ?? throw new ValidationException(L.T("deploy.planNotFound"));
            if (plan.Version != request.Version) throw new ConflictException(L.T("deploy.stale"));
            string script, hash; Compilation? compilation = null;
            if (request.Action == "groups") { var g = Compiler.CompileGroups(plan); script = g.ApplyScript; hash = g.Sha256; }
            else { compilation = request.Action == "rollback" ? await repository.AppliedCompilation(plan.Id,plan.Version) ?? Compiler.Compile(plan) : Compiler.Compile(plan); script = request.Action == "apply" ? compilation.ApplyScript : compilation.RollbackScript; hash = compilation.Sha256; }
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(request.Hash ?? "")))
                throw new ConflictException(L.T("deploy.hash"));
            if (request.Action == "apply") {
                var activeWaves=(await repository.ListDeployments(plan.Id)).Where(r=>r.WaveId is not null).GroupBy(r=>(r.Version,r.WaveId)).Select(g=>g.Last());
                if(activeWaves.Any(r=>r.Action!="wave-rollback" || !r.Succeeded)) throw new ConflictException(L.T("waves.cleanupBeforeApply"));
                if(Compiler.BroadScope(plan) && !request.AcknowledgeScope) throw new ValidationException(L.T("deploy.broadScope"));
            }
            if (request.Action is "apply" or "rollback") {
                var drift = await ReadDrift(plan, request.Action == "apply", compilation, allowMissing: request.Action == "rollback");
                if (request.Action == "rollback" && drift.External.Any(e=>e.Marker==compilation!.Marker)) throw new ConflictException(L.T("drift.renamed"));
                if (!drift.Clean) {
                    if (!request.AcknowledgeDrift) throw new ConflictException(L.T("drift.blocked", Describe(drift)));
                    await repository.AppendDeployment(new(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, hash, "drift-acknowledged", true, JsonSerializer.Serialize(drift, Json.Settings)) { Approval = request.Approval?.Trim() });
                }
            }
            if (request.Action == "apply") await repository.FreezeCompilation(compilation!);
            await repository.AppendDeployment(new(DateTimeOffset.UtcNow,identity,plan.Id,plan.Version,hash,request.Action,false,L.T("deploy.started")){Approval=request.Approval?.Trim(),InProgress=true});
            var ok=false;var output="";
            try { (ok,output)=await runner.Run(script,MutationTimeout(plan)); }
            catch(Exception ex) { output=ex.Message;logger.LogWarning(ex,"Deployment result is inconclusive"); }
            var record = new DeploymentRecord(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, hash, request.Action, ok, output) { Approval = request.Approval?.Trim(), PendingDcs = ok ? PendingDcs(output) : [] };
            await repository.AppendDeployment(record);
            // Hosts keep a removed GPO until their next refresh; refresh them now (failures are recorded, the rollback stands).
            if (ok && request.Action == "rollback") {
                try { await RunHosts(plan, null, "refresh", identity, PlanRules.AfterWaves(plan.Input, plan.Inventory, plan.Input.Waves.Length), studioLast: true); }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Refresh after rollback failed"); }
            }
            return record;
        } finally { gate.Release(); }
    }
    public async Task<DriftReport> Drift(string planId, int version) {
        if (!Enabled) throw new UnauthorizedAccessException(L.T("deploy.disabled"));
        var plan = await repository.Get(planId, version) ?? throw new ValidationException(L.T("deploy.planNotFound"));
        return await ReadDrift(plan, false);
    }
    // Before apply the Studio GPOs are expected to be absent: only external IPsec GPOs count. Before rollback/verify, every GPO must match.
    async Task<DriftReport> ReadDrift(SavedPlan plan, bool beforeApply, Compilation? compilation = null, bool allowMissing = false) {
        var c = compilation ?? await repository.AppliedCompilation(plan.Id,plan.Version) ?? Compiler.Compile(plan);
        var spec = new { domain = plan.Inventory.Domain, target = plan.Input.Settings.LinkTarget == "domain" ? plan.Inventory.DomainDn : plan.Input.Settings.LinkTarget, gpos = c.Gpos.Select(g => g.Name) };
        var (ok, output) = await runner.Run(DriftScript.Replace("__PINPDC__", Compiler.PinPdc).Replace("__DRIFT__", JsonSerializer.Serialize(spec)), TimeSpan.FromMinutes(5));
        if (!ok) throw new InvalidOperationException(L.T("deploy.remoteFailed", output));
        var reading = IpsecStudio.Drift.Parse(output);
        if (reading.Gpos.Length != c.Gpos.Length || !reading.Gpos.Select(g=>g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(c.Gpos.Select(g=>g.Name))) throw new InvalidOperationException(L.T("drift.incomplete"));
        var planHosts = PlanRules.Assess(plan.Input, plan.Inventory).Hosts.Where(h => h.Profile != "dc").Select(h => h.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        reading = reading with { External = reading.External.Select(e => e with { InPlan = e.Computers.Where(planHosts.Contains).ToArray() }).ToArray() };
        var report = IpsecStudio.Drift.Compare(c, reading);
        // Before apply, Studio GPOs must be absent (present ones are drift); before rollback, absent ones are simply nothing to remove.
        var relevant = beforeApply ? report.Gpos.Where(g => g.Exists).Select(g => g with { Differences = g.Differences.Length == 0 ? [L.T("drift.present")] : g.Differences }).ToArray()
                                   : report.Gpos.Where(g => g.Exists).ToArray();
        var absent = report.Gpos.Where(g => !g.Exists).Select(g => g with { Differences = beforeApply ? [] : g.Differences }).ToArray();
        var dirty = relevant.Any(g => g.Differences.Length > 0) || (!beforeApply && !allowMissing && absent.Length > 0) || reading.External.Any(IpsecStudio.Drift.Blocks);
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
            var plan = await repository.Get(request.PlanId, request.Action == "wave-rollback" ? request.Version : null) ?? throw new ValidationException(L.T("deploy.planNotFound"));
            if (plan.Version != request.Version) throw new ConflictException(L.T("deploy.stale"));
            var index = Array.FindIndex(plan.Input.Waves, w => w.Id == request.WaveId);
            if (index < 0) throw new ValidationException(L.T("waves.notFound"));
            var log = await repository.ListDeployments(plan.Id);
            log = log.Where(r => r.Version == plan.Version).ToArray();
            var deployed = log.LastOrDefault(r => r.Action is "apply" or "rollback");
            var apply = request.Action == "wave-apply";
            var hasPolicy = deployed is { Action: "apply", Succeeded: true };
            if (apply && !hasPolicy) throw new ConflictException(L.T("waves.noDeployment"));
            if(apply) {
                var scope=Authorization.Scopes(plan.Input,plan.Inventory);
                if(plan.Input.Waves[index].Members.SelectMany(m=>m.ComputerIds).Any(id=>scope[id]!=true)) throw new ConflictException(L.T("scope.unknown",plan.Input.Waves[index].Members.Sum(m=>m.ComputerIds.Length)));
            }
            if (apply) foreach (var earlier in plan.Input.Waves.Take(index)) {
                var last = log.LastOrDefault(r => r.WaveId == earlier.Id);
                if (last is not { Action: "wave-apply", Succeeded: true, ExpansionAllowed: true }) throw new ConflictException(L.T("waves.blocked", earlier.Name));
            }
            if (!apply) foreach (var later in plan.Input.Waves.Skip(index + 1)) {
                var last = log.LastOrDefault(r => r.WaveId == later.Id);
                if (last is not null && last.Action != "wave-rollback") throw new ConflictException(L.T("waves.reverseOrder", later.Name));
                if (last is { Succeeded: false }) throw new ConflictException(L.T("waves.reverseOrder", later.Name));
            }
            if (apply) {
                if (deployed!.PendingDcs.Length > 0) {
                    var (read, replication)=await runner.Run(Compiler.ReplicationCheck(plan),MutationTimeout(plan));
                    var confirmed=read && ReplicationConfirmed(replication);
                    await repository.AppendDeployment(new(DateTimeOffset.UtcNow,identity,plan.Id,plan.Version,"","replication-check",confirmed,replication){PendingDcs=PendingDcs(replication)});
                    if(!confirmed) throw new ConflictException(L.T("waves.noDeployment"));
                }
                var drift = await ReadDrift(plan,false);
                if (!drift.Clean) throw new ConflictException(L.T("drift.blocked",Describe(drift)));
            }
            var wave = Compiler.CompileWave(plan, request.WaveId, repository.WaveJournalPath(plan.Id, plan.Version, request.WaveId));
            await repository.AppendDeployment(new(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, wave.Sha256, request.Action + "-started", false, "") { Approval=request.Approval?.Trim(), WaveId=wave.WaveId, ExpansionAllowed=false, InProgress=true });
            HostResult[] verify = []; ProbeResult[] probe = []; var expansion = false; var ok = false; var output = ""; string[] pending = []; var blockers=new List<string>();
            try {
                (ok, output) = await runner.Run(apply ? wave.ApplyScript : wave.RollbackScript, MutationTimeout(plan));
                pending = PendingDcs(output);
                var replicated=ok && ReplicationConfirmed(output);
                if(ok && !replicated) { blockers.Add(L.T("waves.replicationIncomplete")); if(!apply) ok=false; }
                if (ok && replicated) {
                    var membership = PlanRules.AfterWaves(plan.Input, plan.Inventory, apply ? index + 1 : index);
                    var refresh = await RunHosts(plan, wave.ComputerIds, "refresh", identity, membership);
                    if (hasPolicy) {
                        verify = await RunHosts(plan, wave.ComputerIds, "verify", identity, membership);
                        // All managed initiators are needed: incoming negative witnesses matter too.
                        var settleSeconds=Math.Clamp(config.GetValue("Deployment:SettleSeconds",120),0,600);
                        if(settleSeconds>0) await Task.Delay(TimeSpan.FromSeconds(settleSeconds));
                        probe = await RunProbe(plan, membership, null, identity);
                    }
                    var expected = TargetHosts(plan, wave.ComputerIds, membership).Select(h => h.Name + "." + plan.Inventory.Domain).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var refreshed = refresh.Length == expected.Count && refresh.Select(h=>h.Host).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(expected) && refresh.All(h=>h.Reachable && RefreshSucceeded(h.Output));
                    var verified = verify.Length == expected.Count && verify.Select(h=>h.Host).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(expected) && verify.All(h=>h.Reachable && IsHealthy(h.Output));
                    var expectedPairs = ProbePairs(plan, membership);
                    var probed = expectedPairs.Count > 0 && probe.Length == expectedPairs.Count && probe.Select(p=>p.Initiator + "\0" + p.Responder).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(expectedPairs) && probe.All(p=>p.Match && p.Conclusive);
                    if(!refreshed) blockers.Add(L.T("waves.refreshIncomplete"));
                    if(hasPolicy && !verified) blockers.Add(L.T("waves.verifyIncomplete"));
                    if(hasPolicy && !probed) blockers.Add(L.T("waves.probeIncomplete"));
                    expansion = replicated && refreshed && verified && probed;
                    if (!apply) ok = refreshed && replicated && (!hasPolicy || verified && probed);
                }
                if(!ok && blockers.Count==0) blockers.Add(L.T("waves.mutationFailed"));
            } catch (Exception ex) when (ex is not OperationCanceledException) { ok=false; blockers.Add(ex.Message); output += "\n" + ex.Message; }
            finally {
                await repository.AppendDeployment(new(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, wave.Sha256, request.Action, ok, output) { Approval=request.Approval?.Trim(), WaveId=wave.WaveId, ExpansionAllowed=apply ? expansion : null, PendingDcs=pending, Blockers=blockers.ToArray() });
            }
            return new(wave.WaveId, wave.Name, ok, apply && expansion, output, verify, probe) { PendingDcs=pending, Blockers=blockers.ToArray() };
        } finally { gate.Release(); }
    }
    public static bool ReplicationConfirmed(string output) {
        try {
            using var d=JsonDocument.Parse(output);
            return d.RootElement.TryGetProperty("replication",out var map) && map.ValueKind==JsonValueKind.Object && map.EnumerateObject().Any() && map.EnumerateObject().All(p=>p.Value.ValueKind==JsonValueKind.String && p.Value.GetString()=="ok");
        } catch(JsonException) { return false; }
    }
    public static TimeSpan MutationTimeout(SavedPlan plan) => TimeSpan.FromSeconds(Math.Max(600, plan.Input.Settings.ReplicationWaitSeconds + 300));
    static bool RefreshSucceeded(string output) { try { using var d=JsonDocument.Parse(output); return d.RootElement.TryGetProperty("gpupdateExit",out var e) && e.GetInt32() == 0; } catch { return false; } }
    static HashSet<string> ProbePairs(SavedPlan plan, InventorySnapshot membership) {
        var a=PlanRules.Assess(plan.Input,membership);
        var responders=a.Hosts.Where(h=>h.Profile is not ("dc" or "none") && h.Ip is not null).Select(h=>(Name:h.Name,Ip:h.Ip)).Concat(plan.Input.ExternalHosts.Select(x=>(Name:x.Name,Ip:(string?)x.Address))).ToArray();
        return TargetHosts(plan,null,membership).SelectMany(i=>responders.Where(r=>r.Ip != i.Ip).Select(r=>i.Name + "\0" + r.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
    static bool IsHealthy(string output) { try { using var doc = JsonDocument.Parse(output); return doc.RootElement.TryGetProperty("healthy", out var h) && h.GetBoolean(); } catch { return false; } }
    // Membership as the AD should look now: inventory plus every wave whose last record is a successful apply.
    async Task<InventorySnapshot> AppliedMembership(SavedPlan plan) {
        var log = await repository.ListDeployments(plan.Id);
        var count = 0;
        foreach (var w in plan.Input.Waves) { var last = log.Where(r => r.Version == plan.Version && r.WaveId == w.Id).LastOrDefault(); if (last is { Action: "wave-apply", Succeeded: true }) count++; else break; }
        return PlanRules.AfterWaves(plan.Input, plan.Inventory, count);
    }
    public async Task<HostResult[]> HostOperation(HostRequest request, string identity) {
        if (!Enabled) throw new UnauthorizedAccessException(L.T("deploy.remoteDisabled"));
        if (request.Action is not ("refresh" or "verify")) throw new ValidationException(L.T("deploy.action"));
        var plan = await repository.Get(request.PlanId,request.Version) ?? throw new ValidationException(L.T("deploy.planNotFound"));
        if (plan.Version != request.Version) throw new ConflictException(L.T("deploy.stale"));
        await gate.WaitAsync();
        try {
            var last=(await repository.ListDeployments(plan.Id)).Where(r=>r.Version==plan.Version).LastOrDefault(r=>r.Action is "apply" or "rollback");
            return await RunHosts(plan, request.ComputerIds, request.Action, identity, await AppliedMembership(plan), expectAbsent:last is {Action:"rollback",Succeeded:true});
        }
        finally { gate.Release(); }
    }
    async Task<HostResult[]> RunHosts(SavedPlan plan, string[]? computerIds, string action, string identity, InventorySnapshot? membership = null, bool studioLast = false, bool expectAbsent = false) {
        var hosts = OrderStudio(TargetHosts(plan, computerIds, membership), h => h.Name, Environment.MachineName, studioLast).ToArray();
        var script = (action == "refresh" ? RefreshScript : VerifyScript).Replace("__HOSTS__", JsonSerializer.Serialize(hosts.Select(h => h.Name + "." + plan.Inventory.Domain)))
            .Replace("__GROUPSIDS__", JsonSerializer.Serialize(ProfileGroupSids(plan))).Replace("__OUTSIDE__", action == "verify" ? OutsideCheck : "")
            .Replace("__STUDIOIPS__", JsonSerializer.Serialize(StudioIps()));
        var (ok, output) = await runner.Run(script, HostsTimeout(hosts.Length));
        await repository.AppendDeployment(new(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, "", action, ok, output));
        if (!ok) throw new InvalidOperationException(L.T("deploy.remoteFailed", output));
        var results = JsonSerializer.Deserialize<HostResult[]>(output, Json.Settings) ?? [];
        return action == "verify" ? results.Select(h => WithIdentity(EffectivePolicy.Enrich(plan, membership ?? plan.Inventory, h, expectAbsent))).ToArray() : results;
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
        var plan = await repository.Get(request.PlanId,request.Version) ?? throw new ValidationException(L.T("deploy.planNotFound"));
        if (plan.Version != request.Version) throw new ConflictException(L.T("deploy.stale"));
        await gate.WaitAsync();
        try { return await RunProbe(plan, await AppliedMembership(plan), request.ComputerIds, identity, request.Capture); }
        finally { gate.Release(); }
    }
    async Task<ProbeResult[]> RunProbe(SavedPlan plan, InventorySnapshot membership, string[]? computerIds, string identity, bool capture = false) {
        var a = PlanRules.Assess(plan.Input, membership);
        // The Studio host probes first: later probes keep their SAs with it (its WinRM session), which would leave asymmetric state.
        var initiators = OrderStudio(TargetHosts(plan, computerIds, membership), h => h.Name, Environment.MachineName, last: false).ToArray();
        var responders = a.Hosts.Where(h => h.Profile is not ("dc" or "none") && h.Ip is not null).Select(h => new { h.ComputerId, h.Name, Ip = h.Ip!, Port = ProbePort })
            .Concat(plan.Input.ExternalHosts.Select(x => new { ComputerId = x.Id, x.Name, Ip = x.Address, Port = ProbePort })).ToArray();
        var studioIps = StudioIps();
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
            var conclusive = expected!="indeterminate" && p.Error is null && (expected != "blocked" || raw.Any(w => w.ResponderId == p.ResponderId && w.Tcp && w.Error is null));
            if(expected=="blocked" && !conclusive) observed="unreachable";
            var detail=p.Error ?? $"tcp={p.Tcp} cipher={p.Cipher ?? "-"} integrity={p.Integrity ?? "-"} auth={p.Auth ?? "-"}" + (!p.Tcp ? " (" + L.T("probe.noConnection") + ")" : "") + (expected=="blocked" && !conclusive ? " " + L.T("probe.needWitness") : "");
            var result = new ProbeResult(p.Initiator, p.Responder, expected, observed, conclusive && expected == observed, detail) { Conclusive = conclusive };
            var host = captured.FirstOrDefault(h => h.Host.Equals(p.Responder, StringComparison.OrdinalIgnoreCase));
            if (host is null || !ips.TryGetValue(p.InitiatorId, out var from) || !ips.TryGetValue(p.ResponderId, out var to)) return result;
            var (esp, clear, attempts) = Wire.Count(host.Buckets ?? [], from, to, ProbePort);
            return result with { Wire = new(esp, clear, attempts, Wire.Judge(expected, esp, clear)) };
        }).ToArray();
    }
    // Observed outcome from one probe: TCP result plus the quick mode SA read on the initiator.
    public static string Classify(bool tcp, string? cipher, string? error) => error is not null ? "unreachable" : !tcp ? "blocked" : cipher is null ? "clear" : cipher == "None" ? "authenticated" : "encrypted";
    sealed record RawProbe(string Initiator, string InitiatorId, string Responder, string ResponderId, bool Tcp, string? Cipher, string? Integrity, string? Auth, string? Error);
    static HostProfile[] TargetHosts(SavedPlan plan, string[]? ids, InventorySnapshot? membership = null) {
        var a = PlanRules.Assess(plan.Input, membership ?? plan.Inventory);
        var hosts = a.Hosts.Where(h => h.Profile != "dc" && h.Profile != "none");
        if (ids is not null) {
            var selected=ids.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if(selected.Count==0 || !selected.IsSubsetOf(hosts.Select(h=>h.ComputerId))) throw new ValidationException(L.T("deploy.scope"));
            hosts = hosts.Where(h=>selected.Contains(h.ComputerId));
        }
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
__PINPDC__
$apply=[guid]'edacfd8f-ffb3-11d1-b41d-00a0c968f939'
$links=@{}
foreach($l in (Get-GPInheritance -Target $spec.target).GpoLinks){ $links[$l.DisplayName]=[bool]$l.Enabled }
$gpos=@()
foreach($name in @($spec.gpos)){
  $g=Get-StudioGpoOrNull $name
  if(-not $g){ $gpos+=@{name=$name;exists=$false;marker=$null;version=0;linked=$false;rules=@();authorizationSddl='';exemptions='';applyGroups=@();denySids=@()}; continue }
  $store=$spec.domain+'\'+$name
  $gs=Open-NetGPO -PolicyStore $store -DomainController $pdc
  $errors=@(); $rules=@(); $setting=$null
  try{ $rules=@(Get-NetIPsecRule -GPOSession $gs -ErrorAction Stop | ForEach-Object { $af=$_|Get-NetFirewallAddressFilter -ErrorAction Stop; $pf=$_|Get-NetFirewallPortFilter -ErrorAction Stop; @{localAddress=(@($af.LocalAddress)-join ',');protocol=[string]$pf.Protocol;localPort=(@($pf.LocalPort)-join ',');remotePort=(@($pf.RemotePort)-join ',');mode=[string]$_.Mode;profile=[string]$_.Profile;phase1AuthSet=[string]$_.Phase1AuthSet;quickModeCryptoSet=[string]$_.QuickModeCryptoSet;name=$_.IPsecRuleName;inbound=$_.InboundSecurity.ToString();outbound=$_.OutboundSecurity.ToString();requireAuthorization=[bool]$_.RequireAuthorization;enabled=($_.Enabled -eq 'True');remoteAddress=(@($af.RemoteAddress) -join ',')} }) }catch{ $errors+="rules: $($_.Exception.Message)" }
  try{ $setting=Get-NetFirewallSetting -GPOSession $gs -ErrorAction Stop }catch{ $errors+="setting: $($_.Exception.Message)" }
  $crypto=$null
  try{
    $mmSets=@(Get-NetIPsecMainModeCryptoSet -GPOSession $gs -ErrorAction Stop)
    $qmSets=@(Get-NetIPsecQuickModeCryptoSet -GPOSession $gs -ErrorAction Stop)
    $p1Sets=@(Get-NetIPsecPhase1AuthSet -GPOSession $gs -ErrorAction Stop)
    $crypto=@{mainMode=@($mmSets | ForEach-Object { $_.Proposal } | ForEach-Object { "$($_.Encryption)-$($_.Hash)-$($_.KeyExchange)" });mainModeMinutes=$(if($mmSets.Count){[int]$mmSets[0].MaxMinutes}else{0});quickMode=@($qmSets | ForEach-Object { $_.Proposal } | ForEach-Object { "$($_.Encapsulation)-$($_.Encryption)-$($_.ESPHash)-$($_.MaxMinutes)" });phase1=@($p1Sets | ForEach-Object { $_.Proposal } | ForEach-Object { "$($_.AuthenticationMethod)" })}
  }catch{ $errors+="crypto: $($_.Exception.Message)" }
  $applying=@(Get-GPPermission -Guid $g.Id -All | Where-Object { $_.Permission -eq 'GpoApply' })
  $perm=@($applying | ForEach-Object { $_.Trustee.Name })
  $applySids=@($applying | ForEach-Object { [string]$_.Trustee.Sid })
  $acl=Get-Acl -Path ('IpsecPdc:\'+$g.Path)
  $deny=@($acl.Access | Where-Object { $_.AccessControlType -eq 'Deny' -and $_.ObjectType -eq $apply } | ForEach-Object { $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value })
  $gpos+=@{name=$name;exists=$true;enabled=($g.GpoStatus -notin @('AllSettingsDisabled','ComputerSettingsDisabled'));marker=$g.Description;version=[int]$g.Computer.DSVersion;linked=[bool]$links[$name];rules=$rules;authorizationSddl=[string]$setting.RemoteMachineTransportAuthorizationList;exemptions=[string]$setting.Exemptions;applyGroups=$perm;applySids=$applySids;denySids=$deny;errors=$errors;crypto=$crypto}
}
# Other GPOs with connection security rules. Rules written by the NetSecurity cmdlets or the firewall snap-in register the
# Windows Firewall extension in gPCMachineExtensionNames (lab, CV4.DS7), so one LDAP query selects the candidates; only those are opened.
# If the Studio's own GPOs exist without that extension the attribute is not trusted here and every GPO is opened.
$fwExt='{B05566AC-FE9C-4368-BE01-7A4CBB6CBA11}'
$base=$domain.DistinguishedName
$all=@(Get-ADObject -LDAPFilter '(objectClass=groupPolicyContainer)' -SearchBase "CN=Policies,CN=System,$base" -Properties displayName,gPCMachineExtensionNames,description)
$ours=@($all | Where-Object { $_.displayName -in @($spec.gpos) })
$filtered=($ours.Count -eq 0) -or (@($ours | Where-Object { "$($_.gPCMachineExtensionNames)" -like "*$fwExt*" }).Count -gt 0)
$candidates=@($all | Where-Object { $_.displayName -notin @($spec.gpos) -and (-not $filtered -or "$($_.gPCMachineExtensionNames)" -like "*$fwExt*") })
$external=@()
foreach($c in $candidates){
  $n=@(Get-NetIPsecRule -PolicyStore ($spec.domain+'\'+$c.displayName) -ErrorAction Stop).Count
  if($n -gt 0){
    $guid=$c.Name
    $pattern='\[LDAP://cn='+[regex]::Escape($guid)+',[^;\]]*;(\d)\]'
    $links=@(Get-ADObject -LDAPFilter "(gPLink=*$guid*)" -SearchBase $base -Properties gPLink | Where-Object { $m=[regex]::Match([string]$_.gPLink,$pattern,'IgnoreCase'); $m.Success -and (([int]$m.Groups[1].Value) -band 1) -eq 0 } | ForEach-Object { $_.DistinguishedName })
    $computers=@($links | ForEach-Object { Get-ADComputer -SearchBase $_ -SearchScope Subtree -Filter * | ForEach-Object { $_.Name } } | Sort-Object -Unique)
    $external+=@{name=$c.displayName;marker=[string]$c.description;ruleCount=$n;links=$links;computers=@($computers | Select-Object -First 500)}
  }
}
@{gpos=$gpos;external=$external;scan=@{total=$all.Count;opened=$candidates.Count;filtered=$filtered}} | ConvertTo-Json -Compress -Depth 6
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
$studioIps=ConvertFrom-Json @'
__STUDIOIPS__
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
    public static string VerifyScriptText => VerifyScript;
    public static string RefreshScriptText => RefreshScript;
    public static string DriftScriptText => DriftScript;
    // Reads the "replication" map the generated scripts print ({dc: ok|timeout|unreachable}) and returns the DCs not "ok".
    public static string[] PendingDcs(string output) {
        try {
            using var doc = JsonDocument.Parse(output);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("replication", out var r) || r.ValueKind != JsonValueKind.Object) return [];
            return r.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() != "ok").Select(p => p.Name).ToArray();
        } catch (JsonException) { return []; }
    }
    public static string ProbeScriptText => ProbeScript;
    public static string CaptureStartScriptText => CaptureStartScript;
    public static string CaptureStopScriptText => CaptureStopScript;
    // gpupdate changes the host's IPsec policy; a WinRM session that crosses that change stalls 60-90 s (lab, CV4.DS5). So:
    // 1) on each host, purge the machine tickets and start gpupdate + SA cleanup as a temporary SYSTEM scheduled task;
    // 2) poll every host with fresh connections until it writes its completion marker. SAs with the Studio host are kept.
    const string RefreshScript = """
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$hosts=ConvertFrom-Json @'
__HOSTS__
'@
$studioIps=ConvertFrom-Json @'
__STUDIOIPS__
'@
$token=[guid]::NewGuid().ToString('N')
# Starts the refresh on the host as a temporary SYSTEM scheduled task: it does not depend on this WinRM session (a WMI-created
# process sometimes never ran, lab CV4.DS7). The task runs gpupdate, clears SAs except the Studio host's and writes a marker.
$launch={
  param($token,$studioIps)
  klist.exe -li 0x3e7 purge | Out-Null
  $dir='C:\ProgramData\IpsecStudio'; New-Item -ItemType Directory -Path $dir -Force | Out-Null
  Get-ScheduledTask -TaskName 'IpsecStudio-Refresh-*' -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false -ErrorAction SilentlyContinue
  $keep=(@($studioIps) | ForEach-Object { "'" + $_ + "'" }) -join ','
  $cmd='gpupdate.exe /target:computer /force /wait:120 | Out-Null; $x=$LASTEXITCODE; ' +
       'Get-NetIPsecMainModeSA -ErrorAction SilentlyContinue | Where-Object {$_.RemoteEndpoint -notin @(' + $keep + ')} | Remove-NetIPsecMainModeSA -ErrorAction SilentlyContinue; ' +
       'Get-NetIPsecQuickModeSA -ErrorAction SilentlyContinue | Where-Object {$_.RemoteEndpoint -notin @(' + $keep + ')} | Remove-NetIPsecQuickModeSA -ErrorAction SilentlyContinue; ' +
       "Set-Content -Path '$dir\refresh-$token.done' -Value `$x"
  $enc=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($cmd))
  $task="IpsecStudio-Refresh-$token"
  $action=New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -NonInteractive -EncodedCommand $enc"
  Register-ScheduledTask -TaskName $task -Action $action -User 'SYSTEM' -RunLevel Highest -Force | Out-Null
  Start-ScheduledTask -TaskName $task
}
$poll={
  param($token)
  $f="C:\ProgramData\IpsecStudio\refresh-$token.done"
  if(Test-Path $f){
    $x=(Get-Content $f -Raw).Trim(); Remove-Item $f -Force
    Unregister-ScheduledTask -TaskName "IpsecStudio-Refresh-$token" -Confirm:$false -ErrorAction SilentlyContinue
    @{computer=$env:COMPUTERNAME;refreshedUtc=(Get-Date).ToUniversalTime().ToString('o');gpupdateExit=[int]$x}
  } else { $null }
}
$results=@()
# One host at a time (Studio host first after apply, last after rollback): start, then wait for the marker with fresh connections.
foreach($h in @($hosts)){
  $done=$null; $last=''; $relaunched=$false
  foreach($round in 1,2){
    $started=$false
    for($attempt=1; $attempt -le 4 -and -not $started; $attempt++){
      try{ Invoke-Command -ComputerName $h -ArgumentList $token,(,@($studioIps)) -ScriptBlock $launch; $started=$true }
      catch{ $last=$_.Exception.Message; Start-Sleep -Seconds 5 }
    }
    if(-not $started){ break }
    $deadline=(Get-Date).AddSeconds(300)
    while(-not $done -and (Get-Date) -lt $deadline){
      try{ $done=Invoke-Command -ComputerName $h -ArgumentList $token -ScriptBlock $poll }catch{ $last=$_.Exception.Message }
      if(-not $done){ Start-Sleep -Seconds 5 }
    }
    if($done){ break }
    $relaunched=$true
  }
  if($done){ $results+=@{host=$h;reachable=$true;output=($done|ConvertTo-Json -Compress)} }
  else{ $results+=@{host=$h;reachable=$false;output="refresh did not report completion in time (relaunched=$relaunched). $last"} }
}
ConvertTo-Json @($results) -Compress -Depth 6
""";
    const string VerifyScript = RemotePreamble + NL + """
    $outsideTarget='__OUTSIDE__'
    $out=Invoke-Command -ComputerName $h -ArgumentList $lastChange,$outsideTarget -ScriptBlock {
      param($lastChangeUtc,$outsideTarget)
      # Reachability outside the secure subnet (null when not configured): outbound Require on the subnet was seen to cut it.
      $outside=$null
      if($outsideTarget){ $ip,$port=$outsideTarget -split ':'; $c=New-Object Net.Sockets.TcpClient; try{ $a=$c.BeginConnect($ip,[int]$port,$null,$null); $outside=($a.AsyncWaitHandle.WaitOne(5000) -and $c.Connected) }catch{ $outside=$false } finally{ $c.Close() } }
      $tgt=$null
      try{ $text=(klist.exe -li 0x3e7 tgt 2>&1 | Out-String);  if($text -match 'Start\s*Time\s*:\s*([^\r\n]+?)\s*\(local\)'){ $tgt=[datetime]::Parse($matches[1],[Globalization.CultureInfo]::CurrentCulture).ToUniversalTime().ToString('o') } }catch{}
      $rules=@(Get-NetIPsecRule -PolicyStore ActiveStore -TracePolicyStore | Where-Object DisplayName -like 'IpsecStudio*' | ForEach-Object { $af=$_ | Get-NetFirewallAddressFilter; $pf=$_ | Get-NetFirewallPortFilter; @{localAddress=(@($af.LocalAddress)-join ',');protocol=[string]$pf.Protocol;localPort=(@($pf.LocalPort)-join ',');remotePort=(@($pf.RemotePort)-join ',');mode=[string]$_.Mode;profile=[string]$_.Profile;phase1AuthSet=[string]$_.Phase1AuthSet;quickModeCryptoSet=[string]$_.QuickModeCryptoSet;remoteAddress=(@($af.RemoteAddress) -join ',');name=$_.IPsecRuleName;display=$_.DisplayName;inbound=$_.InboundSecurity.ToString();outbound=$_.OutboundSecurity.ToString();requireAuthorization=[bool]$_.RequireAuthorization;source=$_.PolicyStoreSource;enabled=$_.Enabled.ToString();status=$_.PrimaryStatus.ToString();statusText=[string]$_.Status}})
      $setting=Get-NetFirewallSetting -PolicyStore ActiveStore
      $mm=@(Get-NetIPsecMainModeCryptoSet -PolicyStore ActiveStore | Where-Object Name -eq '{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE1}')
      $qm=@(Get-NetIPsecQuickModeCryptoSet -PolicyStore ActiveStore | Where-Object Name -eq 'IpsecStudio-QM')
      $p1=@(Get-NetIPsecPhase1AuthSet -PolicyStore ActiveStore | Where-Object Name -eq 'IpsecStudio-P1')
      $crypto=@{mainMode=@($mm | ForEach-Object {$_.Proposal} | ForEach-Object {"$($_.Encryption)-$($_.Hash)-$($_.KeyExchange)"});mainModeMinutes=$(if($mm.Count){[int]$mm[0].MaxMinutes}else{0});quickMode=@($qm | ForEach-Object {$_.Proposal} | ForEach-Object {"$($_.Encapsulation)-$($_.Encryption)-$($_.ESPHash)-$($_.MaxMinutes)"});phase1=@($p1 | ForEach-Object {$_.Proposal} | ForEach-Object {"$($_.AuthenticationMethod)"})}
      $all=@(Get-NetIPsecRule -PolicyStore ActiveStore | Where-Object Enabled -eq True).Count
      @{computer=$env:COMPUTERNAME;studioRules=$rules;crypto=$crypto;enabledIpsecRules=$all;authorizationList=[string]$setting.RemoteMachineTransportAuthorizationList;exemptions=[string]$setting.Exemptions;gpos=@($rules | ForEach-Object {$_.source} | Sort-Object -Unique);healthy=(@($rules | Where-Object {$_.status -ne 'OK'}).Count -eq 0 -and $rules.Count -gt 0 -and $outside -ne $false);outside=$outside;tgtStartUtc=$tgt;lastGroupChangeUtc=$lastChangeUtc}
    }
""" + NL + RemoteEpilogue;
    // Starts an NDIS packet capture on each responder. Refuses a host where another trace is already running.
    const string CaptureStartScript = """
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$hosts=ConvertFrom-Json @'
__HOSTS__
'@
# All hosts at once (Invoke-Command fans out): one slow host must not delay the others (lab, CV4.DS5: 7 hosts in sequence timed out).
$errs=$null
$started=@{}
foreach($o in @(Invoke-Command -ComputerName @($hosts | ForEach-Object { $_.fqdn }) -ErrorAction SilentlyContinue -ErrorVariable errs -ScriptBlock {
      $dir='C:\ProgramData\IpsecStudio\capture'; New-Item -ItemType Directory -Path $dir -Force | Out-Null
      $status=(netsh.exe trace show status) -join ' '
      if($status -notmatch 'no trace session'){ throw "another trace session is running" }
      Remove-Item (Join-Path $dir 'probe.*') -Force -ErrorAction SilentlyContinue
      $out=netsh.exe trace start capture=yes report=no persistent=no maxsize=128 overwrite=yes tracefile=(Join-Path $dir 'probe.etl')
      if($LASTEXITCODE -ne 0){ throw "netsh trace start failed: $($out -join ' ')" }
      'started'
    })){ $started[[string]$o.PSComputerName]=$true }
$results=@(foreach($h in @($hosts)){
  if($started[$h.fqdn]){ @{host=$h.name;ok=$true;error=$null;bytes=0;sha256=$null;buckets=@()} }
  else{ $e=@($errs | Where-Object { "$($_.OriginInfo.PSComputerName)" -eq $h.fqdn -or "$($_.TargetObject)" -eq $h.fqdn } | ForEach-Object { $_.Exception.Message } | Select-Object -First 1); @{host=$h.name;ok=$false;error="$e";bytes=0;sha256=$null;buckets=@()} }
})
ConvertTo-Json @($results) -Compress -Depth 4
""";
    // Stops the capture, decodes it with pktmon, buckets IPv4 packets (source, destination, port, esp/tcp) and deletes the files.
    const string CaptureStopScript = """
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$spec=ConvertFrom-Json @'
__SPEC__
'@
$errs=$null
$byHost=@{}
foreach($r in @(Invoke-Command -ComputerName @($spec.hosts | ForEach-Object { $_.fqdn }) -ErrorAction SilentlyContinue -ErrorVariable errs -ScriptBlock {
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
    })){ $byHost[[string]$r.PSComputerName]=$r }
$results=@(foreach($h in @($spec.hosts)){
  $r=$byHost[$h.fqdn]
  if($r){ @{host=$h.name;ok=[bool]$r.ok;error=$r.error;bytes=[long]$r.bytes;sha256=$r.sha256;buckets=@($r.buckets)} }
  else{ $e=@($errs | Where-Object { "$($_.OriginInfo.PSComputerName)" -eq $h.fqdn -or "$($_.TargetObject)" -eq $h.fqdn } | ForEach-Object { $_.Exception.Message } | Select-Object -First 1); @{host=$h.name;ok=$false;error="$e";bytes=0;sha256=$null;buckets=@()} }
})
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
        # With outbound Request the first SYN goes out in clear while IKE negotiates; a responder that only requests IPsec accepts it,
        # so TCP may complete before the SA. Wait up to 3 s (the Windows fallback window) before calling it clear (lab, CV4.DS5).
        $qm=@()
        for($w=0; $w -lt 12; $w++){ Start-Sleep -Milliseconds 250; $qm=@(Get-NetIPsecQuickModeSA -ErrorAction SilentlyContinue | Where-Object RemoteEndpoint -eq $t.ip | Select-Object -First 1); if($qm.Count -or -not $tcp){ break } }
        # Clear only if a second connection also gets no SA (right after a rollout one attempt was seen without SA, then fine).
        if($tcp -and -not $qm.Count){
          Start-Sleep -Seconds 2
          $client=New-Object System.Net.Sockets.TcpClient
          try{$async=$client.BeginConnect($t.ip,[int]$t.port,$null,$null);[void]$async.AsyncWaitHandle.WaitOne(6000)}catch{}finally{$client.Close()}
          for($w=0; $w -lt 12; $w++){ Start-Sleep -Milliseconds 250; $qm=@(Get-NetIPsecQuickModeSA -ErrorAction SilentlyContinue | Where-Object RemoteEndpoint -eq $t.ip | Select-Object -First 1); if($qm.Count){ break } }
        }
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
