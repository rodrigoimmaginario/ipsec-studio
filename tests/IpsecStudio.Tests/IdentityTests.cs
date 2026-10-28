using System.Text.Json;
using IpsecStudio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IpsecStudio.Tests;

// The IPsec peer identity comes from the machine's Kerberos ticket (logon session 0x3e7). A host is "pending" when its TGT
// predates the last change of any profile group in AD; "synced" when the TGT is newer; "unknown" without a reading.
public class IdentityTests {
    static readonly DateTimeOffset T0 = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(10, 5, "synced")]
    [InlineData(5, 10, "pending")]
    [InlineData(5, 5, "synced")]
    public void Identity_state_compares_tgt_start_with_the_last_group_change(int tgtMinutes, int changeMinutes, string expected) =>
        Assert.Equal(expected, Deployment.IdentityState(T0.AddMinutes(tgtMinutes), T0.AddMinutes(changeMinutes)));

    [Fact]
    public void Missing_readings_are_unknown() {
        Assert.Equal("unknown", Deployment.IdentityState(null, T0));
        Assert.Equal("unknown", Deployment.IdentityState(T0, null));
    }

    [Fact]
    public async Task Verify_enriches_host_results_with_identity_state() {
        var f = new LabFixture();
        var folder = Path.Combine(Path.GetTempPath(), "ipsec-identity-" + LabFixture.Id());
        try {
            var repo = new PlanRepository(folder);
            var saved = await repo.Save(null, f.Plan, f.Snapshot, PlanRules.Assess(f.Plan, f.Snapshot), "t");
            var runner = new FakeRunner { Reply = s => (true, JsonSerializer.Serialize(new[] {
                new { host = "app1.lab.ipsec.test", reachable = true, output = JsonSerializer.Serialize(new { healthy = true, studioRules = Array.Empty<object>(), tgtStartUtc = T0.AddMinutes(10).ToString("o"), lastGroupChangeUtc = T0.ToString("o") }) },
                new { host = "db1.lab.ipsec.test", reachable = true, output = JsonSerializer.Serialize(new { healthy = true, studioRules = Array.Empty<object>(), tgtStartUtc = T0.AddMinutes(-10).ToString("o"), lastGroupChangeUtc = T0.ToString("o") }) },
                new { host = "fs1.lab.ipsec.test", reachable = false, output = "WinRM failed" } })) };
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Deployment:Enabled"] = "true" }).Build();
            var d = new Deployment(config, repo, runner, NullLogger<Deployment>.Instance);
            var r = await d.HostOperation(new(saved.Id, 1, "verify"), "op");
            Assert.Equal(["synced", "pending", "unknown"], r.Select(h => h.IdentityState));
            Assert.Equal(T0.AddMinutes(-10), r[1].TgtStartUtc); Assert.Equal(T0, r[1].LastGroupChangeUtc);
            var script = runner.Scripts.Single();
            Assert.Contains("klist.exe -li 0x3e7 tgt", script);
            Assert.Contains("whenChanged", script);
            Assert.Contains(f.SqlClients.Sid!, script); // profile group SIDs are passed to read whenChanged
        } finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    sealed class FakeRunner : IScriptRunner {
        public readonly List<string> Scripts = [];
        public Func<string, (bool, string)> Reply = _ => (true, "[]");
        public Task<(bool Ok, string Output)> Run(string script, TimeSpan timeout) { Scripts.Add(script); return Task.FromResult(Reply(script)); }
    }
}
