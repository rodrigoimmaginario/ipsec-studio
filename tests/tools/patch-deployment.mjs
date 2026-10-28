// One-off patch for CV4.DS1: IScriptRunner, approval note, groups and waves in Deployment. Safe to delete after use.
import fs from 'node:fs';
const read = (f) => fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n');
const rep = (s, a, b) => { if (!s.includes(a)) throw new Error('missing: ' + a.slice(0, 70)); return s.replace(a, b); };
let s = read('app/IpsecStudio/Deployment.cs');
s = rep(s, `using System.Diagnostics;
using System.Security.Cryptography;`, `using System.Security.Cryptography;`);
s = rep(s, `public sealed record DeployRequest(string PlanId, int Version, string Hash, string Action);
public sealed record HostRequest(string PlanId, int Version, string Action, string[]? ComputerIds = null);
public sealed record DeploymentRecord(DateTimeOffset Utc, string Identity, string PlanId, int Version, string Hash, string Action, bool Succeeded, string Output);`,
`public sealed record DeployRequest(string PlanId, int Version, string Hash, string Action, string? Approval = null);
public sealed record HostRequest(string PlanId, int Version, string Action, string[]? ComputerIds = null);
public sealed record WaveRequest(string PlanId, int Version, string WaveId, string Action, string? Approval = null);
public sealed record DeploymentRecord(DateTimeOffset Utc, string Identity, string PlanId, int Version, string Hash, string Action, bool Succeeded, string Output) {
    public string? Approval { get; init; }
    public string? WaveId { get; init; }
    public bool? ExpansionAllowed { get; init; }
}
public sealed record WaveResult(string WaveId, string WaveName, bool Succeeded, bool ExpansionAllowed, string Output, HostResult[] Verify, ProbeResult[] Probe);`);
s = rep(s, `public sealed class Deployment(IConfiguration config, PlanRepository repository, ILogger<Deployment> logger) {`,
`public sealed class Deployment(IConfiguration config, PlanRepository repository, IScriptRunner runner, ILogger<Deployment> logger) {`);
s = rep(s, `        if (request.Action is not ("apply" or "rollback")) throw new ValidationException(L.T("deploy.action"));
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
    }`,
`        if (request.Action is not ("apply" or "rollback" or "groups")) throw new ValidationException(L.T("deploy.action"));
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
            var (ok, output) = await runner.Run(script, TimeSpan.FromMinutes(10));
            var record = new DeploymentRecord(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, hash, request.Action, ok, output) { Approval = request.Approval?.Trim() };
            await repository.AppendDeployment(record);
            return record;
        } finally { gate.Release(); }
    }
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
    }`);
s = rep(s, `        var plan = await repository.Get(request.PlanId) ?? throw new ValidationException(L.T("deploy.planNotFound"));
        if (plan.Version != request.Version) throw new ConflictException(L.T("deploy.stale"));
        var hosts = TargetHosts(plan, request.ComputerIds);
        var script = (request.Action == "refresh" ? RefreshScript : VerifyScript).Replace("__HOSTS__", JsonSerializer.Serialize(hosts.Select(h => h.Name + "." + plan.Inventory.Domain)));
        var (ok, output) = await RunPowerShell(script, TimeSpan.FromMinutes(6));
        await repository.AppendDeployment(new(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, "", request.Action, ok, output));
        if (!ok) throw new InvalidOperationException(L.T("deploy.remoteFailed", output));
        return JsonSerializer.Deserialize<HostResult[]>(output, Json.Settings) ?? [];
    }`,
`        var plan = await repository.Get(request.PlanId) ?? throw new ValidationException(L.T("deploy.planNotFound"));
        if (plan.Version != request.Version) throw new ConflictException(L.T("deploy.stale"));
        return await RunHosts(plan, request.ComputerIds, request.Action, identity);
    }
    async Task<HostResult[]> RunHosts(SavedPlan plan, string[]? computerIds, string action, string identity) {
        var hosts = TargetHosts(plan, computerIds);
        var script = (action == "refresh" ? RefreshScript : VerifyScript).Replace("__HOSTS__", JsonSerializer.Serialize(hosts.Select(h => h.Name + "." + plan.Inventory.Domain)));
        var (ok, output) = await runner.Run(script, TimeSpan.FromMinutes(6));
        await repository.AppendDeployment(new(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, "", action, ok, output));
        if (!ok) throw new InvalidOperationException(L.T("deploy.remoteFailed", output));
        return JsonSerializer.Deserialize<HostResult[]>(output, Json.Settings) ?? [];
    }`);
s = rep(s, `        var plan = await repository.Get(request.PlanId) ?? throw new ValidationException(L.T("deploy.planNotFound"));
        if (plan.Version != request.Version) throw new ConflictException(L.T("deploy.stale"));
        var a = PlanRules.Assess(plan.Input, plan.Inventory);
        var initiators = TargetHosts(plan, request.ComputerIds);`,
`        var plan = await repository.Get(request.PlanId) ?? throw new ValidationException(L.T("deploy.planNotFound"));
        if (plan.Version != request.Version) throw new ConflictException(L.T("deploy.stale"));
        return await RunProbe(plan, await AppliedMembership(plan), request.ComputerIds, identity);
    }
    async Task<ProbeResult[]> RunProbe(SavedPlan plan, InventorySnapshot membership, string[]? computerIds, string identity) {
        var a = PlanRules.Assess(plan.Input, membership);
        var initiators = TargetHosts(plan, computerIds);`);
s = rep(s, `        var (ok, output) = await RunPowerShell(ProbeScript.Replace("__SPEC__", JsonSerializer.Serialize(spec)), TimeSpan.FromMinutes(10));`,
`        var (ok, output) = await runner.Run(ProbeScript.Replace("__SPEC__", JsonSerializer.Serialize(spec)), TimeSpan.FromMinutes(10));`);
s = rep(s, `            var expected = p.Error is not null ? "indeterminate" : Simulator.Evaluate(plan.Input, plan.Inventory, a, p.InitiatorId, p.ResponderId).Outcome;`,
`            var expected = p.Error is not null ? "indeterminate" : Simulator.Evaluate(plan.Input, membership, a, p.InitiatorId, p.ResponderId).Outcome;`);
// remove RunPowerShell method
const start = s.indexOf('    async Task<(bool Ok, string Output)> RunPowerShell(');
const end = s.indexOf('    const string RemotePreamble');
if (start < 0 || end < 0) throw new Error('RunPowerShell block not found');
s = s.slice(0, start) + s.slice(end);
fs.writeFileSync('app/IpsecStudio/Deployment.cs', s);

let p = read('app/IpsecStudio/Program.cs');
p = rep(p, `builder.Services.AddSingleton<Deployment>();`, `builder.Services.AddSingleton<IScriptRunner, PowerShellRunner>();
builder.Services.AddSingleton<Deployment>();`);
p = rep(p, `app.MapPost("/api/plans/simulate", async (PlanInput input, DirectoryReader directory, PlanRepository repository) => {
    var (snapshot, assessment) = await Evaluate(input, directory, repository);
    return new { assessment, matrix = Simulator.Matrix(input, snapshot, assessment) };
});`,
`app.MapPost("/api/plans/simulate", async (PlanInput input, DirectoryReader directory, PlanRepository repository, HttpContext context) => {
    var (snapshot, assessment) = await Evaluate(input, directory, repository);
    var waves = int.TryParse(context.Request.Query["wave"], out var n) ? Math.Clamp(n, 0, input.Waves?.Length ?? 0) : 0;
    var membership = PlanRules.AfterWaves(input, snapshot, waves);
    var waveAssessment = waves == 0 ? assessment : PlanRules.Assess(input, membership);
    return new { assessment, wave = waves, matrix = Simulator.Matrix(input, membership, waveAssessment), waveHosts = waveAssessment.Hosts };
});`);
p = rep(p, `app.MapGet("/api/plans/{id}/compile", async (string id, PlanRepository repository) => { var saved = await repository.Get(id) ?? throw new ValidationException(L.T("api.planNotFound")); return Compiler.Compile(saved); });`,
`app.MapGet("/api/plans/{id}/compile", async (string id, PlanRepository repository) => { var saved = await repository.Get(id) ?? throw new ValidationException(L.T("api.planNotFound")); return Compiler.Compile(saved); });
app.MapGet("/api/plans/{id}/compile-groups", async (string id, PlanRepository repository) => { var saved = await repository.Get(id) ?? throw new ValidationException(L.T("api.planNotFound")); return Compiler.CompileGroups(saved); });
app.MapGet("/api/plans/{id}/compile-wave/{waveId}", async (string id, string waveId, PlanRepository repository) => { var saved = await repository.Get(id) ?? throw new ValidationException(L.T("api.planNotFound")); return Compiler.CompileWave(saved, waveId); });
app.MapPost("/api/wave", async (WaveRequest request, Deployment deployment) => await deployment.ApplyWave(request, identity));`);
p = rep(p, `const string Version = "0.3.0";`, `const string Version = "0.4.0";`);
fs.writeFileSync('app/IpsecStudio/Program.cs', p);
console.log('patched');
