using IpsecStudio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IpsecStudio.Tests;

// Lab, 29/10 (Test-OutboundRequire.ps1 on fs1): outbound Require on the local secure subnet made Windows drop traffic leaving the
// subnet (internet, Azure DNS/WireServer). Require/Request did not. The Studio therefore never emits outbound Require by default.
public class OutboundTests : IDisposable {
    readonly LabFixture f = new();
    readonly string folder = Path.Combine(Path.GetTempPath(), "ipsec-outbound-" + LabFixture.Id());
    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

    [Fact]
    public void Default_plan_compiles_without_outbound_require_and_without_alert() {
        var c = Compiler.Compile(f.Saved());
        Assert.All(c.Gpos, g => Assert.NotEqual("Require", g.Outbound));
        Assert.All(c.Gpos.Where(g => g.Profile is "restricted" or "quarantine"), g => Assert.Equal(("Require", "Request"), (g.Inbound, g.Outbound)));
        Assert.DoesNotContain(PlanRules.Assess(f.Plan, f.Snapshot).Warnings, w => w.Contains("Require"));
    }

    [Fact]
    public void Explicit_option_restores_outbound_require_with_a_clear_alert() {
        var strict = f.Plan with { Settings = f.Plan.Settings with { RequireOutbound = true } };
        var c = Compiler.Compile(f.Saved(strict));
        Assert.All(c.Gpos.Where(g => g.Profile is "restricted" or "quarantine"), g => Assert.Equal("Require", g.Outbound));
        Assert.Equal("Request", c.Gpos.Single(g => g.Profile == "base").Outbound);
        L.Lang = "pt";
        try {
            var w = Assert.Single(PlanRules.Assess(strict, f.Snapshot).Warnings, x => x.Contains("saída 'Require'"));
            Assert.Contains("Banco de dados", w); Assert.Contains("Arquivos", w); Assert.Contains("Quarentena", w);
            Assert.Contains("fora da sub-rede segura", w); Assert.Contains("Não recomendado", w);
        } finally { L.Lang = "pt"; }
    }

    [Fact]
    public void Base_require_mode_is_alerted_too() {
        var w = PlanRules.Assess(f.Plan with { BaseMode = "require" }, f.Snapshot).Warnings;
        Assert.Contains(w, x => x.Contains("saída 'Require'") && x.Contains("Base"));
    }

    [Fact]
    public void Simulator_follows_the_outbound_mode() {
        Assert.Equal("clear", f.Outcome(f.Db1.Id, f.Ext1.Id));       // restricted server falls back to clear towards a host without IPsec
        var strict = f.Plan with { Settings = f.Plan.Settings with { RequireOutbound = true } };
        Assert.Equal("blocked", f.Outcome(f.Db1.Id, f.Ext1.Id, strict));
        var s = f.With(f.Exclusion with { MemberIds = [f.ClientNo.Id] });
        Assert.Equal("clear", f.Outcome(f.ClientNo.Id, f.Legacy.Id, snapshot: s));   // quarantine still reaches hosts without IPsec...
        Assert.Equal("blocked", f.Outcome(f.ClientNo.Id, f.Legacy.Id, strict, s));   // ...unless outbound Require is chosen explicitly
        Assert.Equal("blocked", f.Outcome(f.ClientNo.Id, f.Db1.Id, snapshot: s));    // protected hosts keep refusing it on their side
    }

    [Fact]
    public async Task Verify_tests_the_configured_outside_target_and_rejects_invalid_ones() {
        var repo = new PlanRepository(folder);
        var saved = await repo.Save(null, f.Plan, f.Snapshot, PlanRules.Assess(f.Plan, f.Snapshot), "t");
        var runner = new FakeRunner();
        Deployment D(string? outside) => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Deployment:Enabled"] = "true", ["Deployment:OutsideCheck"] = outside }).Build(), repo, runner, NullLogger<Deployment>.Instance);
        await D("168.63.129.16:80").HostOperation(new(saved.Id, 1, "verify"), "op");
        Assert.Contains("'168.63.129.16:80'", runner.Scripts.Last());
        await D(null).HostOperation(new(saved.Id, 1, "verify"), "op");
        Assert.Contains("$outsideTarget=''", runner.Scripts.Last());
        await Assert.ThrowsAsync<ValidationException>(() => D("evil';calc;'").HostOperation(new(saved.Id, 1, "verify"), "op"));
    }

    [Fact]
    public async Task Rollback_refreshes_the_hosts_and_survives_a_refresh_failure() {
        var repo = new PlanRepository(folder);
        var saved = await repo.Save(null, f.Plan, f.Snapshot, PlanRules.Assess(f.Plan, f.Snapshot), "t");
        var c = Compiler.Compile(saved);
        var runner = new FakeRunner { Reply = s => s.Contains("gpupdate") ? (false, "host down") : (true, "{}") };
        var d = new Deployment(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Deployment:Enabled"] = "true" }).Build(), repo, runner, NullLogger<Deployment>.Instance);
        var r = await d.Execute(new(saved.Id, 1, c.Sha256, "rollback", ""), "op");
        Assert.True(r.Succeeded);
        var refresh = runner.Scripts.Single(s => s.Contains("gpupdate"));
        Assert.Contains("\"db1.lab.ipsec.test\"", refresh); Assert.DoesNotContain("dc1.lab.ipsec.test", refresh);
        var log = await repo.ListDeployments(saved.Id);
        Assert.Contains(log, e => e.Action == "refresh" && !e.Succeeded);
    }

    [Fact]
    public void Studio_host_is_refreshed_last() =>
        Assert.Equal(["app1", "db1", "admin1"], Deployment.StudioLast(["admin1", "app1", "db1"], h => h, "ADMIN1").ToArray());

    sealed class FakeRunner : IScriptRunner {
        public readonly List<string> Scripts = [];
        public Func<string, (bool, string)> Reply = _ => (true, "[]");
        public Task<(bool Ok, string Output)> Run(string script, TimeSpan timeout) { Scripts.Add(script); return Task.FromResult(Reply(script)); }
    }
}
