using System.Text.Json;
using IpsecStudio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IpsecStudio.Tests;

// Drift: what is in AD versus what the Studio compiled; external GPOs carrying IPsec rules in scope.
public class DriftTests : IDisposable {
    readonly LabFixture f = new();
    readonly string folder = Path.Combine(Path.GetTempPath(), "ipsec-drift-" + LabFixture.Id());
    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

    static DriftGpoReading Reading(Compilation c, GpoSpec g, string marker, string[] exempt, bool linked = true) => new(g.Name, true, marker, 3, linked,
        new[] { new DriftRuleReading("IpsecStudio-Exempt", "None", "None", false, string.Join(",", exempt)), new DriftRuleReading("IpsecStudio-Main", g.Inbound, g.Outbound, g.RequireAuthorization, "10.74.1.0/24") },
        g.AuthorizationSddl ?? "", "Icmp", [g.ApplyGroup], g.DenyGroupSids) { Crypto = Drift.ExpectedCrypto(c, g) };
    static DriftReading Clean(Compilation c) => new(c.Gpos.Select(g => Reading(c, g, c.Marker, c.ExemptAddresses)).ToArray(), []);

    [Fact]
    public void Identical_state_is_clean() {
        var c = Compiler.Compile(f.Saved());
        var r = Drift.Compare(c, Clean(c));
        Assert.True(r.Clean); Assert.All(r.Gpos, g => Assert.Empty(g.Differences)); Assert.Empty(r.External);
    }

    [Fact]
    public void Edited_rule_missing_gpo_wrong_marker_and_filtering_are_reported() {
        var c = Compiler.Compile(f.Saved());
        var clean = Clean(c);
        var edited = clean.Gpos.Select(g => g.Name == "IpsecStudio-Base"
            ? g with { Rules = g.Rules.Select(r => r.Name == "IpsecStudio-Main" ? r with { Inbound = "Request" } : r).ToArray(), ApplyGroups = ["Domain Computers", "Domain Users"] }
            : g.Name.StartsWith("IpsecStudio-Restricted-Banco") ? g with { Marker = "IpsecStudio plan other v9", AuthorizationSddl = "O:LSD:" }
            : g.Profile() == "boundary" ? g with { Exists = false } : g).ToArray();
        var r = Drift.Compare(c, clean with { Gpos = edited });
        Assert.False(r.Clean);
        var b = r.Gpos.Single(g => g.Name == "IpsecStudio-Base");
        Assert.Contains(b.Differences, d => d.Contains("IpsecStudio-Main") && d.Contains("Request") && d.Contains("Require"));
        Assert.Contains(b.Differences, d => d.Contains("Domain Users"));
        var s = r.Gpos.Single(g => g.Name.StartsWith("IpsecStudio-Restricted-Banco"));
        Assert.Contains(s.Differences, d => d.Contains("other v9"));
        Assert.Contains(s.Differences, d => d.Contains("O:LSD:"));
        Assert.Contains(r.Gpos.Single(g => g.Name == "IpsecStudio-Boundary").Differences, d => d.Contains("não existe") || d.Contains("missing"));
        Assert.Empty(r.Gpos.Single(g => g.Name == "IpsecStudio-Management").Differences);
    }

    // Formats confirmed on dc1 (Probe-CryptoReadout.ps1): proposals as Windows enum names, quick mode in preference order.
    [Fact]
    public void Expected_crypto_uses_the_windows_readout_format() {
        var c = Compiler.Compile(f.Saved());
        var b = Drift.ExpectedCrypto(c, c.Gpos.Single(g => g.Name == "IpsecStudio-Base"));
        Assert.Equal(["AES256-SHA256-DH14"], b.MainMode); Assert.Equal(180, b.MainModeMinutes);
        Assert.Equal(["ESP-None-SHA256-60", "ESP-AES256-SHA256-60"], b.QuickMode);
        Assert.Equal(["MachineKerb"], b.Phase1);
        var encrypted = c.Gpos.First(g => g.Encryption);
        Assert.Equal(["ESP-AES256-SHA256-60"], Drift.ExpectedCrypto(c, encrypted).QuickMode);
    }

    [Fact]
    public void Crypto_authentication_and_icmp_changes_are_reported() {
        var c = Compiler.Compile(f.Saved());
        var clean = Clean(c);
        var edited = clean.Gpos.Select(g => g.Name == "IpsecStudio-Base"
            ? g with { Crypto = g.Crypto! with { QuickMode = ["ESP-None-SHA256-60", "ESP-AES128-SHA256-60"], MainModeMinutes = 480 }, Exemptions = "NotConfigured" }
            : g.Name == "IpsecStudio-Boundary" ? g with { Crypto = g.Crypto! with { Phase1 = ["MachineKerb", "MachineNtlm"] } }
            : g.Name == "IpsecStudio-Management" ? g with { Crypto = null } : g).ToArray();
        var r = Drift.Compare(c, clean with { Gpos = edited });
        Assert.False(r.Clean);
        var b = r.Gpos.Single(g => g.Name == "IpsecStudio-Base").Differences;
        Assert.Contains(b, d => d.Contains("IpsecStudio-QM") && d.Contains("ESP-AES128-SHA256-60"));
        Assert.Contains(b, d => d.Contains("IpsecStudio-MM") && d.Contains("480"));
        Assert.Contains(b, d => d.Contains("ICMP"));
        Assert.Contains(r.Gpos.Single(g => g.Name == "IpsecStudio-Boundary").Differences, d => d.Contains("IpsecStudio-P1") && d.Contains("MachineNtlm"));
        Assert.NotEmpty(r.Gpos.Single(g => g.Name == "IpsecStudio-Management").Differences);
    }

    [Fact]
    public void NotConfigured_authorization_list_matches_a_gpo_without_one() {
        var c = Compiler.Compile(f.Saved());
        var reading = Clean(c) with { Gpos = Clean(c).Gpos.Select(g => g.Name == "IpsecStudio-Boundary" ? g with { AuthorizationSddl = "NotConfigured" } : g).ToArray() };
        Assert.Null(c.Gpos.Single(g => g.Name == "IpsecStudio-Boundary").AuthorizationSddl);
        Assert.True(Drift.Compare(c, reading).Clean);
    }

    [Fact]
    public void External_gpos_with_ipsec_rules_make_the_report_dirty() {
        var c = Compiler.Compile(f.Saved());
        var r = Drift.Compare(c, Clean(c) with { External = [new("Corp-Legacy-IPsec", 2, ["DC=lab,DC=ipsec,DC=test"])] });
        Assert.False(r.Clean); Assert.Single(r.External); Assert.Equal("Corp-Legacy-IPsec", r.External[0].Name);
    }

    [Fact]
    public async Task Apply_and_rollback_are_gated_by_drift_unless_acknowledged() {
        var repo = new PlanRepository(folder);
        var saved = await repo.Save(null, f.Plan, f.Snapshot, PlanRules.Assess(f.Plan, f.Snapshot), "t");
        var c = Compiler.Compile(saved);
        var dirty = JsonSerializer.Serialize(new { gpos = Array.Empty<object>(), external = new[] { new { name = "Corp-Legacy-IPsec", ruleCount = 1, links = new[] { "DC=lab,DC=ipsec,DC=test" } } } }, Json.Settings);
        var runner = new FakeRunner { Reply = s => s.Contains("@{gpos=$gpos;external=$external") ? (true, dirty) : (true, "{}") };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Deployment:Enabled"] = "true" }).Build();
        var d = new Deployment(config, repo, runner, NullLogger<Deployment>.Instance);
        var report = await d.Drift(saved.Id, 1);
        Assert.False(report.Clean); Assert.Single(report.External);
        await Assert.ThrowsAsync<ConflictException>(() => d.Execute(new(saved.Id, 1, c.Sha256, "apply", "ok"), "op"));
        Assert.DoesNotContain(runner.Scripts, s => s.Contains("New-GPO"));
        var r = await d.Execute(new(saved.Id, 1, c.Sha256, "apply", "ok") { AcknowledgeDrift = true }, "op");
        Assert.True(r.Succeeded);
        var log = await repo.ListDeployments(saved.Id);
        Assert.Contains(log, e => e.Action == "drift-acknowledged" && e.Output.Contains("Corp-Legacy-IPsec"));
        Assert.Contains(runner.Scripts, s => s.Contains("New-GPO"));
        runner.Reply = s => s.Contains("@{gpos=$gpos;external=$external") ? (true, JsonSerializer.Serialize(new { gpos = c.Gpos.Select(g => new { name = g.Name, exists = true, marker = c.Marker, version = 1, linked = true, rules = new[] { new { name = "IpsecStudio-Exempt", inbound = "None", outbound = "None", requireAuthorization = false, remoteAddress = "10.74.1.10" }, new { name = "IpsecStudio-Main", inbound = g.Inbound, outbound = g.Outbound, requireAuthorization = g.RequireAuthorization, remoteAddress = "10.74.1.0/24" } }, authorizationSddl = g.AuthorizationSddl ?? "", exemptions = "Icmp", applyGroups = new[] { g.ApplyGroup }, denySids = g.DenyGroupSids, crypto = Drift.ExpectedCrypto(c, g) }), external = Array.Empty<object>() }, Json.Settings)) : (true, "{}");
        var clean = await d.Drift(saved.Id, 1);
        Assert.True(clean.Clean);
        var rb = await d.Execute(new(saved.Id, 1, c.Sha256, "rollback", ""), "op");
        Assert.True(rb.Succeeded);
    }

    sealed class FakeRunner : IScriptRunner {
        public readonly List<string> Scripts = [];
        public Func<string, (bool, string)> Reply = _ => (true, "{}");
        public Task<(bool Ok, string Output)> Run(string script, TimeSpan timeout) { Scripts.Add(script); return Task.FromResult(Reply(script)); }
    }
}
static class DriftReadingExtensions {
    public static string Profile(this DriftGpoReading g) => g.Name.Contains("Boundary") ? "boundary" : g.Name.Contains("Management") ? "management" : g.Name.Contains("Quarantine") ? "quarantine" : g.Name.Contains("Restricted") ? "restricted" : "base";
}
