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
        public Func<string, (bool, string)> Reply = ScriptReplies.Default;
        public Task<(bool Ok, string Output)> Run(string script, TimeSpan timeout) { Scripts.Add(script); return Task.FromResult(Reply(script)); }
    }
    readonly LabFixture f = new();
    readonly string folder = Path.Combine(Path.GetTempPath(), "ipsec-deploy-" + LabFixture.Id());
    readonly PlanRepository repo; readonly FakeRunner runner = new(); readonly Deployment deployment; readonly SavedPlan saved;
    public DeploymentTests() {
        repo = new PlanRepository(folder);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Deployment:Enabled"] = "true", ["Deployment:ProbePort"] = "5985", ["Deployment:SettleSeconds"]="0" }).Build();
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
        await Assert.ThrowsAsync<ValidationException>(() => deployment.Execute(new(saved.Id,1,c.Sha256,"apply","approved"),"op"));
        Assert.Empty(runner.Scripts);
        var r = await deployment.Execute(new(saved.Id, 1, c.Sha256, "apply", "Aprovado pelo operador") { AcknowledgeScope=true }, "LAB\\op");
        Assert.True(r.Succeeded); Assert.Equal("Aprovado pelo operador", r.Approval); Assert.Equal("LAB\\op", r.Identity);
        Assert.Equal(2, runner.Scripts.Count); Assert.Contains("external=$external", runner.Scripts[0]); Assert.Contains("New-GPO", runner.Scripts[1]);
        var log = await repo.ListDeployments(saved.Id);
        Assert.Equal(2,log.Length); Assert.True(log[0].InProgress); Assert.False(log[1].InProgress); Assert.Equal(c.Sha256, log[0].Hash);
        runner.Reply = s => s.Contains("external=$external") ? ScriptReplies.Default(s) : (false, "boom");
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
        Assert.Contains("{E5A5D32A-4BCE-4e4d-B07F-4AB1BA7E5FE1}",script); // -Default assigns the documented MM set identity, not the supplied display/name label.
        await Assert.ThrowsAsync<ValidationException>(() => deployment.HostOperation(new(saved.Id, 1, "verify", [f.Dc1.Id]), "op"));
        await Assert.ThrowsAsync<ValidationException>(() => deployment.HostOperation(new(saved.Id, 1, "verify", []), "op"));
        await Assert.ThrowsAsync<ValidationException>(() => deployment.HostOperation(new(saved.Id, 1, "verify", [f.App1.Id,f.Dc1.Id]), "op"));
        await Assert.ThrowsAsync<ValidationException>(() => deployment.Probe(new(saved.Id, 1, "probe", []), "op"));
        await Assert.ThrowsAsync<ValidationException>(() => deployment.HostOperation(new(saved.Id, 1, "reboot"), "op"));
        runner.Reply = _ => (false, "fatal");
        await Assert.ThrowsAsync<InvalidOperationException>(() => deployment.HostOperation(new(saved.Id, 1, "refresh"), "op"));
    }

    [Fact]
    public async Task Historical_revision_can_be_verified_and_probed_after_a_new_edit() {
        await repo.Save(saved.Id,f.Plan with {ExpectedVersion=1,Name="Later edit"},f.Snapshot,saved.Assessment,"op");
        runner.Reply=EvidenceReply(saved,f.Snapshot,f.App1.Id);
        var hosts=await deployment.HostOperation(new(saved.Id,1,"verify",[f.App1.Id]),"op");
        using var policy=JsonDocument.Parse(Assert.Single(hosts).Output);Assert.True(policy.RootElement.GetProperty("healthy").GetBoolean());
        var probe=await deployment.Probe(new(saved.Id,1,"probe",[f.App1.Id]),"op");
        Assert.NotEmpty(probe);
    }
    [Fact]
    public async Task Probe_compares_observed_with_simulated_outcomes() {
        runner.Reply = _ => (true, JsonSerializer.Serialize(new[] {
            new { initiator = "app1", initiatorId = f.App1.Id, responder = "db1", responderId = f.Db1.Id, tcp = true, cipher = (string?)"AES256", integrity = (string?)"SHA256", auth = (string?)"Kerberos", error = (string?)null },
            new { initiator = "client-no", initiatorId = f.ClientNo.Id, responder = "db1", responderId = f.Db1.Id, tcp = true, cipher = (string?)"None", integrity = (string?)"SHA256", auth = (string?)"Kerberos", error = (string?)null },
            new { initiator = "fs1", initiatorId = f.Fs1.Id, responder = "ext1", responderId = f.Ext1.Id, tcp = true, cipher = (string?)null, integrity = (string?)null, auth = (string?)null, error = (string?)null } }));
        var r = await deployment.Probe(new(saved.Id, 1, "probe"), "op");
        Assert.Equal(["encrypted", "blocked", "clear"], r.Select(x => x.Expected)); // restricted falls back to clear towards ext1 (no outbound Require)
        Assert.Equal(["encrypted", "authenticated", "clear"], r.Select(x => x.Observed));
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
        await repo.AppendDeployment(new(DateTimeOffset.UtcNow,"op",p.Id,1,"","apply",true,"{}"));
        runner.Reply = EvidenceReply(p, PlanRules.AfterWaves(plan,snapshot,1), f.App1.Id);
        var r = await deployment.ApplyWave(new(p.Id, 1, w1.Id, "wave-apply", "Piloto aprovado"), "op");
        Assert.True(r.Succeeded); Assert.True(r.ExpansionAllowed); Assert.Equal("Piloto", r.WaveName);
        Assert.Contains(runner.Scripts, s => s.Contains("Add-ADGroupMember -Identity $p.g -Members $p.c") && s.Contains(f.App1.Sid!));
        var refreshScript = runner.Scripts.Single(s => s.Contains("gpupdate"));
        Assert.Contains("app1.lab.ipsec.test", refreshScript); Assert.DoesNotContain("client-no.lab.ipsec.test", refreshScript);
        Assert.All(r.Probe, x => Assert.True(x.Match));
        // Wave 2 with a probe mismatch: recorded as failed, expansion blocked, and wave 3 (none here) would be refused.
        runner.Reply = EvidenceReply(p,PlanRules.AfterWaves(plan,snapshot,2),f.ClientNo.Id,bad:true);
        var r2 = await deployment.ApplyWave(new(p.Id, 1, w2.Id, "wave-apply", "Resto aprovado"), "op");
        Assert.False(r2.ExpansionAllowed); Assert.Contains(r2.Probe, x => !x.Match);
        await Assert.ThrowsAsync<ValidationException>(() => deployment.ApplyWave(new(p.Id, 1, w2.Id, "wave-apply", ""), "op"));
        runner.Reply = EvidenceReply(p,PlanRules.AfterWaves(plan,snapshot,1),f.ClientNo.Id);
        var rb = await deployment.ApplyWave(new(p.Id, 1, w2.Id, "wave-rollback", ""), "op");
        Assert.True(rb.Succeeded); Assert.Contains(runner.Scripts, s => s.Contains("Remove-ADGroupMember -Identity $e.g -Members $e.c") && s.Contains(f.ClientNo.Sid!));
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
        await repo.AppendDeployment(new(DateTimeOffset.UtcNow,"op",p.Id,1,"","apply",true,"{}"));
        runner.Reply=EvidenceReply(p,PlanRules.AfterWaves(plan,f.Snapshot,1),f.ClientNo.Id,bad:true);
        var r1 = await deployment.ApplyWave(new(p.Id, 1, w1.Id, "wave-apply", "ok"), "op");
        Assert.False(r1.ExpansionAllowed);
        await Assert.ThrowsAsync<ConflictException>(() => deployment.ApplyWave(new(p.Id, 1, w2.Id, "wave-apply", "ok"), "op"));
        await deployment.ApplyWave(new(p.Id, 1, w1.Id, "wave-rollback", ""), "op");
        // Reverting A is not proof that A passed: B remains blocked until A is reapplied and validated.
        await Assert.ThrowsAsync<ConflictException>(()=>deployment.ApplyWave(new(p.Id,1,w2.Id,"wave-apply","ok"),"op"));
        runner.Reply=EvidenceReply(p,PlanRules.AfterWaves(plan,f.Snapshot,1),f.ClientNo.Id);
        Assert.True((await deployment.ApplyWave(new(p.Id,1,w1.Id,"wave-apply","ok"),"op")).ExpansionAllowed);
        runner.Reply=EvidenceReply(p,PlanRules.AfterWaves(plan,f.Snapshot,2),f.ClientNo.Id);
        var r2 = await deployment.ApplyWave(new(p.Id, 1, w2.Id, "wave-apply", "ok"), "op");
        Assert.True(r2.ExpansionAllowed);
    }
    [Fact]
    public async Task A_single_host_with_no_probe_pairs_cannot_release_expansion() {
        var s=f.Snapshot with {Objects=[f.PilotOu,f.App1,f.Dc1,f.DomainComputers with {MemberIds=[f.App1.Id]}]};
        var w=new Wave{Id=LabFixture.Id(),Name="Single",Members=[new(){GroupId=f.DomainComputers.Id,ComputerIds=[f.App1.Id]}]};
        var input=new PlanInput{Name="Single",InventoryId=s.Id,BaseGroupId=f.DomainComputers.Id,Waves=[w]};
        var p=await repo.Save(null,input,s,PlanRules.Assess(input,s),"op");
        await repo.AppendDeployment(new(DateTimeOffset.UtcNow,"op",p.Id,1,"","apply",true,"{}"));
        runner.Reply=EvidenceReply(p,s,f.App1.Id);
        var result=await deployment.ApplyWave(new(p.Id,1,w.Id,"wave-apply","approved"),"op");
        Assert.False(result.ExpansionAllowed);Assert.Empty(result.Probe);Assert.NotEmpty(result.Blockers);
    }
    [Fact]
    public async Task Negative_TCP_without_a_positive_witness_is_not_a_green_match() {
        var raw=JsonSerializer.Serialize(new[]{new{initiator=f.ClientNo.Name,initiatorId=f.ClientNo.Id,responder=f.Db1.Name,responderId=f.Db1.Id,tcp=false,cipher=(string?)null,integrity=(string?)null,auth=(string?)null,error=(string?)null}});
        runner.Reply=_=>(true,raw);
        var result=Assert.Single(await deployment.Probe(new(saved.Id,1,"probe"),"op"));
        Assert.False(result.Match);Assert.False(result.Conclusive);Assert.Equal("unreachable",result.Observed);
    }
    static Func<string,(bool,string)> EvidenceReply(SavedPlan p,InventorySnapshot s,string hostId,bool bad=false) {
        var host=s.Objects.Single(o=>o.Id==hostId); var c=Compiler.Compile(p with {Inventory=s});
        var verify=JsonSerializer.Serialize(new[]{new HostResult(host.Name+"."+s.Domain,true,EffectivePolicyTests.Output(c,c.Gpos.Single(g=>g.ComputerIds.Contains(hostId)),host.Name))});
        var refresh=JsonSerializer.Serialize(new[]{new HostResult(host.Name+"."+s.Domain,true,"{\"gpupdateExit\":0}")});
        var ids=s.Objects.Where(PlanRules.IsComputer).ToDictionary(o=>o.Name,o=>o.Id);
        foreach(var x in p.Input.ExternalHosts) ids[x.Name]=x.Id;
        var external=p.Input.ExternalHosts.Select(x=>x.Name).ToHashSet();
        var matrix=Simulator.Matrix(p.Input,s,PlanRules.Assess(p.Input,s)).Where(e=>!external.Contains(e.Initiator)).ToArray();
        var probe=JsonSerializer.Serialize(matrix.Select((e,i)=>new {initiator=e.Initiator,initiatorId=ids[e.Initiator],responder=e.Responder,responderId=ids[e.Responder],tcp=bad && i==0 ? e.Outcome=="blocked" : e.Outcome!="blocked",cipher=e.Outcome=="encrypted" ? "AES256" : e.Outcome=="authenticated" ? "None" : (string?)null,integrity="SHA256",auth="Kerberos",error=(string?)null}));
        return script=>script.Contains("external=$external") ? (true,ScriptReplies.CleanGpos(c)) : script.Contains("ADGroupMember") ? (true,"{\"replication\":{\"dc.test\":\"ok\"}}") : script.Contains("gpupdate") ? (true,refresh) : script.Contains("ActiveStore") ? (true,verify) : (true,probe);
    }
}
