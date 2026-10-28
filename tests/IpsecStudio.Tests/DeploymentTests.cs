using System.Text.Json;
using IpsecStudio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IpsecStudio.Tests;

// Orchestration tested through a fake script runner: no PowerShell, no AD, no WinRM.
public class DeploymentTests : IDisposable {
    sealed class FakeRunner : IScriptRunner {
        public readonly List<string> Scripts = [];
        public Func<string, (bool, string)> Reply = _ => (true, "{}");
        public Task<(bool Ok, string Output)> Run(string script, TimeSpan timeout) { Scripts.Add(script); return Task.FromResult(Reply(script)); }
    }
    readonly LabFixture f = new();
    readonly string folder = Path.Combine(Path.GetTempPath(), "ipsec-deploy-" + LabFixture.Id());
    readonly PlanRepository repo; readonly FakeRunner runner = new(); readonly Deployment deployment; readonly SavedPlan saved;
    public DeploymentTests() {
        repo = new PlanRepository(folder);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Deployment:Enabled"] = "true", ["Deployment:ProbePort"] = "5985" }).Build();
        deployment = new Deployment(config, repo, runner, NullLogger<Deployment>.Instance);
        saved = repo.Save(null, f.Plan, f.Snapshot, PlanRules.Assess(f.Plan, f.Snapshot), "t").Result;
    }
    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

    [Fact]
    public async Task Apply_requires_matching_hash_current_version_and_an_approval_note() {
        var c = Compiler.Compile(saved);
        await Assert.ThrowsAsync<ConflictException>(() => deployment.Execute(new(saved.Id, 1, "0".PadLeft(64, '0'), "apply", "ok"), "op"));
        await Assert.ThrowsAsync<ConflictException>(() => deployment.Execute(new(saved.Id, 2, c.Sha256, "apply", "ok"), "op"));
        await Assert.ThrowsAsync<ValidationException>(() => deployment.Execute(new(saved.Id, 1, c.Sha256, "apply", ""), "op"));
        await Assert.ThrowsAsync<ValidationException>(() => deployment.Execute(new(saved.Id, 1, c.Sha256, "explode", "ok"), "op"));
        Assert.Empty(runner.Scripts);
        var r = await deployment.Execute(new(saved.Id, 1, c.Sha256, "apply", "Aprovado pelo operador"), "LAB\\op");
        Assert.True(r.Succeeded); Assert.Equal("Aprovado pelo operador", r.Approval); Assert.Equal("LAB\\op", r.Identity);
        Assert.Single(runner.Scripts); Assert.Contains("New-GPO", runner.Scripts[0]);
        var log = await repo.ListDeployments(saved.Id);
        Assert.Single(log); Assert.Equal(c.Sha256, log[0].Hash);
        runner.Reply = _ => (false, "boom");
        var failed = await deployment.Execute(new(saved.Id, 1, c.Sha256, "rollback", ""), "op");
        Assert.False(failed.Succeeded); Assert.Equal("boom", failed.Output);
    }

    [Fact]
    public async Task Disabled_instance_refuses_everything() {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Deployment:Enabled"] = "false" }).Build();
        var d = new Deployment(config, repo, runner, NullLogger<Deployment>.Instance);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => d.Execute(new(saved.Id, 1, "x", "apply", "ok"), "op"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => d.HostOperation(new(saved.Id, 1, "verify"), "op"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => d.Probe(new(saved.Id, 1, "probe"), "op"));
    }

    [Fact]
    public async Task Host_operations_target_only_managed_hosts_and_parse_results() {
        runner.Reply = s => (true, JsonSerializer.Serialize(new[] { new { host = "app1.lab.ipsec.test", reachable = true, output = "{\"healthy\":true}" }, new { host = "db1.lab.ipsec.test", reachable = false, output = "WinRM failed" } }));
        var r = await deployment.HostOperation(new(saved.Id, 1, "verify", [f.App1.Id, f.Db1.Id]), "op");
        Assert.Equal(2, r.Length); Assert.False(r[1].Reachable);
        var script = runner.Scripts.Single();
        Assert.Contains("\"app1.lab.ipsec.test\"", script); Assert.Contains("\"db1.lab.ipsec.test\"", script); Assert.DoesNotContain("dc1.lab.ipsec.test", script);
        Assert.Contains("Get-NetIPsecRule -PolicyStore ActiveStore", script);
        await Assert.ThrowsAsync<ValidationException>(() => deployment.HostOperation(new(saved.Id, 1, "verify", [f.Dc1.Id]), "op"));
        await Assert.ThrowsAsync<ValidationException>(() => deployment.HostOperation(new(saved.Id, 1, "reboot"), "op"));
        runner.Reply = _ => (false, "fatal");
        await Assert.ThrowsAsync<InvalidOperationException>(() => deployment.HostOperation(new(saved.Id, 1, "refresh"), "op"));
    }

    [Fact]
    public async Task Probe_compares_observed_with_simulated_outcomes() {
        runner.Reply = _ => (true, JsonSerializer.Serialize(new[] {
            new { initiator = "app1", initiatorId = f.App1.Id, responder = "db1", responderId = f.Db1.Id, tcp = true, cipher = (string?)"AES256", integrity = (string?)"SHA256", auth = (string?)"Kerberos", error = (string?)null },
            new { initiator = "client-no", initiatorId = f.ClientNo.Id, responder = "db1", responderId = f.Db1.Id, tcp = true, cipher = (string?)"None", integrity = (string?)"SHA256", auth = (string?)"Kerberos", error = (string?)null },
            new { initiator = "fs1", initiatorId = f.Fs1.Id, responder = "ext1", responderId = f.Ext1.Id, tcp = false, cipher = (string?)null, integrity = (string?)null, auth = (string?)null, error = (string?)null } }));
        var r = await deployment.Probe(new(saved.Id, 1, "probe"), "op");
        Assert.Equal(["encrypted", "blocked", "blocked"], r.Select(x => x.Expected));
        Assert.Equal(["encrypted", "authenticated", "blocked"], r.Select(x => x.Observed));
        Assert.Equal([true, false, true], r.Select(x => x.Match));
        var script = runner.Scripts.Single();
        Assert.Contains("\"ip\":\"10.74.1.60\"", script.ToLowerInvariant().Replace(" ", "")); // external host is a responder
        Assert.Contains("Remove-NetIPsecMainModeSA", script);
    }

    [Fact]
    public async Task Wave_apply_adds_members_runs_the_wave_hosts_and_stops_expansion_on_mismatch() {
        var sqlClients = f.SqlClients with { MemberIds = [f.Admin1.Id] };
        var snapshot = f.Snapshot with { Objects = f.Snapshot.Objects.Select(o => o.Id == sqlClients.Id ? sqlClients : o).ToArray() };
        var w1 = new Wave { Id = LabFixture.Id(), Name = "Piloto", Members = [new() { GroupId = f.SqlClients.Id, ComputerIds = [f.App1.Id] }] };
        var w2 = new Wave { Id = LabFixture.Id(), Name = "Resto", Members = [new() { GroupId = f.SqlClients.Id, ComputerIds = [f.ClientNo.Id] }] };
        var plan = f.Plan with { Waves = [w1, w2] };
        var p = await repo.Save(null, plan, snapshot, PlanRules.Assess(plan, snapshot), "t");
        var probeOk = JsonSerializer.Serialize(new[] { new { initiator = "app1", initiatorId = f.App1.Id, responder = "db1", responderId = f.Db1.Id, tcp = true, cipher = (string?)"AES256", integrity = (string?)"SHA256", auth = (string?)"Kerberos", error = (string?)null } });
        var verifyOk = JsonSerializer.Serialize(new[] { new { host = "app1.lab.ipsec.test", reachable = true, output = "{\"healthy\":true,\"studioRules\":[]}" } });
        runner.Reply = s => s.Contains("Add-ADGroupMember") ? (true, "{\"added\":1}") : s.Contains("gpupdate") ? (true, verifyOk) : s.Contains("Get-NetIPsecRule -PolicyStore ActiveStore") ? (true, verifyOk) : (true, probeOk);
        var r = await deployment.ApplyWave(new(p.Id, 1, w1.Id, "wave-apply", "Piloto aprovado"), "op");
        Assert.True(r.Succeeded); Assert.True(r.ExpansionAllowed); Assert.Equal("Piloto", r.WaveName);
        Assert.Contains(runner.Scripts, s => s.Contains($"Add-ADGroupMember -Identity '{f.SqlClients.Sid}' -Members '{f.App1.Sid}'"));
        var refreshScript = runner.Scripts.Single(s => s.Contains("gpupdate"));
        Assert.Contains("app1.lab.ipsec.test", refreshScript); Assert.DoesNotContain("client-no.lab.ipsec.test", refreshScript);
        Assert.All(r.Probe, x => Assert.True(x.Match));
        // Wave 2 with a probe mismatch: recorded as failed, expansion blocked, and wave 3 (none here) would be refused.
        var probeBad = JsonSerializer.Serialize(new[] { new { initiator = "client-no", initiatorId = f.ClientNo.Id, responder = "db1", responderId = f.Db1.Id, tcp = false, cipher = (string?)null, integrity = (string?)null, auth = (string?)null, error = (string?)null } });
        runner.Reply = s => s.Contains("Add-ADGroupMember") ? (true, "{}") : (s.Contains("gpupdate") || s.Contains("ActiveStore")) ? (true, verifyOk.Replace("app1", "client-no")) : (true, probeBad);
        var r2 = await deployment.ApplyWave(new(p.Id, 1, w2.Id, "wave-apply", "Resto aprovado"), "op");
        Assert.False(r2.ExpansionAllowed); Assert.Contains(r2.Probe, x => !x.Match);
        await Assert.ThrowsAsync<ValidationException>(() => deployment.ApplyWave(new(p.Id, 1, w2.Id, "wave-apply", ""), "op"));
        var rb = await deployment.ApplyWave(new(p.Id, 1, w2.Id, "wave-rollback", ""), "op");
        Assert.True(rb.Succeeded); Assert.Contains(runner.Scripts, s => s.Contains($"Remove-ADGroupMember -Identity '{f.SqlClients.Sid}' -Members '{f.ClientNo.Sid}'"));
        var log = await repo.ListDeployments(p.Id);
        Assert.Contains(log, e => e.Action == "wave-apply" && e.Approval == "Piloto aprovado");
        Assert.Contains(log, e => e.Action == "wave-rollback");
    }

    [Fact]
    public async Task Second_wave_is_refused_while_the_previous_one_failed_and_was_not_rolled_back() {
        var w1 = new Wave { Id = LabFixture.Id(), Name = "A", Members = [new() { GroupId = f.SqlClients.Id, ComputerIds = [f.ClientNo.Id] }] };
        var w2 = new Wave { Id = LabFixture.Id(), Name = "B", Members = [new() { GroupId = f.FileClients.Id, ComputerIds = [f.ClientNo.Id] }] };
        var plan = f.Plan with { Waves = [w1, w2] };
        var p = await repo.Save(null, plan, f.Snapshot, PlanRules.Assess(plan, f.Snapshot), "t");
        var bad = JsonSerializer.Serialize(new[] { new { initiator = "client-no", initiatorId = f.ClientNo.Id, responder = "db1", responderId = f.Db1.Id, tcp = false, cipher = (string?)null, integrity = (string?)null, auth = (string?)null, error = (string?)null } });
        var verify = JsonSerializer.Serialize(new[] { new { host = "client-no.lab.ipsec.test", reachable = true, output = "{\"healthy\":true,\"studioRules\":[]}" } });
        runner.Reply = s => s.Contains("ADGroupMember") ? (true, "{}") : (s.Contains("gpupdate") || s.Contains("ActiveStore")) ? (true, verify) : (true, bad);
        var r1 = await deployment.ApplyWave(new(p.Id, 1, w1.Id, "wave-apply", "ok"), "op");
        Assert.False(r1.ExpansionAllowed);
        await Assert.ThrowsAsync<ConflictException>(() => deployment.ApplyWave(new(p.Id, 1, w2.Id, "wave-apply", "ok"), "op"));
        await deployment.ApplyWave(new(p.Id, 1, w1.Id, "wave-rollback", ""), "op");
        var ok = JsonSerializer.Serialize(new[] { new { initiator = "client-no", initiatorId = f.ClientNo.Id, responder = "fs1", responderId = f.Fs1.Id, tcp = true, cipher = (string?)"None", integrity = (string?)"SHA256", auth = (string?)"Kerberos", error = (string?)null } });
        runner.Reply = s => s.Contains("ADGroupMember") ? (true, "{}") : (s.Contains("gpupdate") || s.Contains("ActiveStore")) ? (true, verify) : (true, ok);
        // After rollback of wave A, wave B may proceed (A is no longer a failed applied wave).
        var r2 = await deployment.ApplyWave(new(p.Id, 1, w2.Id, "wave-apply", "ok"), "op");
        Assert.True(r2.ExpansionAllowed);
    }
}
