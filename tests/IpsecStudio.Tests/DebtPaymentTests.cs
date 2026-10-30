using System.Text.Json;
using IpsecStudio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IpsecStudio.Tests;

// CV4.DS7: configurable replication wait with pending DCs surfaced; fast search for other IPsec GPOs and who they reach.
public class DebtPaymentTests : IDisposable {
    readonly LabFixture f = new();
    readonly string folder = Path.Combine(Path.GetTempPath(), "ipsec-ds7-" + LabFixture.Id());
    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

    [Fact]
    public void Replication_wait_is_a_plan_setting_used_by_gpo_and_wave_scripts() {
        var waveId = LabFixture.Id();
        var plan = f.Plan with { Settings = f.Plan.Settings with { ReplicationWaitSeconds = 900 },
            Waves = [new Wave { Id = waveId, Name = "Piloto", Members = [new WaveMember { GroupId = f.SqlClients.Id, ComputerIds = [f.ClientNo.Id] }] }] };
        var c = Compiler.Compile(f.Saved(plan));
        Assert.Contains("\"replicationWaitSeconds\":900", c.ApplyScript);
        Assert.Contains("AddSeconds([int]$spec.replicationWaitSeconds)", c.ApplyScript);
        Assert.Contains("AddSeconds(900)", Compiler.CompileWave(f.Saved(plan), waveId).ApplyScript);
        Assert.Equal(180, new PlanSettings().ReplicationWaitSeconds);
        Assert.Throws<ValidationException>(() => PlanRules.Assess(f.Plan with { Settings = f.Plan.Settings with { ReplicationWaitSeconds = 3601 } }, f.Snapshot));
        Assert.Throws<ValidationException>(() => PlanRules.Assess(f.Plan with { Settings = f.Plan.Settings with { ReplicationWaitSeconds = -1 } }, f.Snapshot));
    }

    [Theory]
    [InlineData("{\"marker\":\"m\",\"replication\":{\"dc1.lab.ipsec.test\":\"ok\",\"dc2.lab.ipsec.test\":\"timeout\",\"dc3.lab.ipsec.test\":\"unreachable\"}}", new[] { "dc2.lab.ipsec.test", "dc3.lab.ipsec.test" })]
    [InlineData("{\"marker\":\"m\",\"replication\":{\"dc1.lab.ipsec.test\":\"ok\"}}", new string[0])]
    [InlineData("{\"marker\":\"m\"}", new string[0])]
    [InlineData("not json", new string[0])]
    [InlineData("[1,2]", new string[0])]
    public void Pending_dcs_are_read_from_the_script_output(string output, string[] pending) => Assert.Equal(pending, Deployment.PendingDcs(output));

    [Fact]
    public async Task Apply_record_carries_the_dcs_that_did_not_confirm() {
        var repo = new PlanRepository(folder);
        var saved = await repo.Save(null, f.Plan, f.Snapshot, PlanRules.Assess(f.Plan, f.Snapshot), "t");
        var c = Compiler.Compile(saved);
        var runner = new Runner { Reply = s => s.Contains("New-GPO") ? (true, "{\"marker\":\"m\",\"replication\":{\"dc1\":\"ok\",\"dc2\":\"timeout\"}}") : (true, "{}") };
        var d = new Deployment(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Deployment:Enabled"] = "true" }).Build(), repo, runner, NullLogger<Deployment>.Instance);
        var r = await d.Execute(new(saved.Id, 1, c.Sha256, "apply", "ok"), "op");
        Assert.Equal(["dc2"], r.PendingDcs);
    }

    [Fact]
    public async Task Other_ipsec_gpos_report_the_computers_they_reach_and_the_plan_hosts_among_them() {
        var repo = new PlanRepository(folder);
        var saved = await repo.Save(null, f.Plan, f.Snapshot, PlanRules.Assess(f.Plan, f.Snapshot), "t");
        var reading = JsonSerializer.Serialize(new {
            gpos = Array.Empty<object>(),
            external = new[] { new { name = "Corp-Legacy", ruleCount = 2, links = new[] { "OU=Servers,DC=lab,DC=ipsec,DC=test" }, computers = new[] { "db1", "printsrv" } } },
            scan = new { total = 312, opened = 5, filtered = true } }, Json.Settings);
        var runner = new Runner { Reply = s => s.Contains("@{gpos=$gpos;external=$external") ? (true, reading) : (true, "{}") };
        var d = new Deployment(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Deployment:Enabled"] = "true" }).Build(), repo, runner, NullLogger<Deployment>.Instance);
        var r = await d.Drift(saved.Id, 1);
        var e = Assert.Single(r.External);
        Assert.Equal(["db1", "printsrv"], e.Computers);
        Assert.Equal(["db1"], e.InPlan);
        Assert.Equal(new DriftScan(312, 5, true), r.Scan);
    }

    [Fact]
    public void Search_opens_only_gpos_with_the_firewall_extension_and_reads_links_and_computers() {
        var s = Deployment.DriftScriptText;
        Assert.Contains("{B05566AC-FE9C-4368-BE01-7A4CBB6CBA11}", s);
        Assert.Contains("gPCMachineExtensionNames", s);
        Assert.Contains("(gPLink=*$guid*)", s);
        Assert.Contains("Get-ADComputer -SearchBase", s);
        Assert.DoesNotContain("Get-GPO -All", s);
    }

    sealed class Runner : IScriptRunner {
        public Func<string, (bool, string)> Reply = _ => (true, "{}");
        public Task<(bool Ok, string Output)> Run(string script, TimeSpan timeout) => Task.FromResult(Reply(script));
    }
}

public class UnlinkedExternalTests {
    // CV4.DS7 measurement: an unlinked GPO with IPsec rules reaches no computer; it is listed but does not block apply/rollback.
    [Fact]
    public void Unlinked_external_gpos_are_listed_without_blocking() {
        var unlinked = new DriftExternalReading("Lab-Bulk-Ipsec-001", 1, []);
        var linked = new DriftExternalReading("Corp-Legacy", 1, ["DC=lab,DC=ipsec,DC=test"]) { Computers = ["db1"] };
        Assert.False(Drift.Blocks(unlinked));
        Assert.True(Drift.Blocks(linked));
    }
}
