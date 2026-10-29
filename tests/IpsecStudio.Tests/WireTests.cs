using System.Text.Json;
using IpsecStudio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IpsecStudio.Tests;

// Packet capture on probe responders: ESP versus clear TCP per pair, as evidence independent from the SA read on the initiator.
public class WireTests : IDisposable {
    readonly LabFixture f = new();
    readonly string folder = Path.Combine(Path.GetTempPath(), "ipsec-wire-" + LabFixture.Id());
    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

    [Theory]
    [InlineData("encrypted", 10, 0, true)]
    [InlineData("authenticated", 4, 0, true)]
    [InlineData("encrypted", 0, 0, false)]
    [InlineData("encrypted", 10, 2, false)]
    [InlineData("clear", 0, 6, true)]
    [InlineData("clear", 3, 6, true)]
    [InlineData("clear", 0, 0, false)]
    [InlineData("blocked", 0, 0, true)]
    [InlineData("blocked", 5, 0, true)]
    [InlineData("blocked", 0, 2, false)]
    public void Judge_requires_esp_for_protected_pairs_and_clear_tcp_for_clear_pairs(string expected, int esp, int clear, bool match) =>
        Assert.Equal(match, Wire.Judge(expected, esp, clear));

    [Fact]
    public void Indeterminate_and_unreachable_are_not_judged() {
        Assert.Null(Wire.Judge("indeterminate", 3, 0));
        Assert.Null(Wire.Judge("unreachable", 0, 0));
    }

    [Fact]
    public void Count_separates_esp_clear_acceptance_and_clear_attempts() {
        WireBucket[] b = [
            new("10.74.1.20", "10.74.1.30", 0, "esp", 7), new("10.74.1.30", "10.74.1.20", 0, "esp", 5),
            new("10.74.1.20", "10.74.1.30", 5985, "syn", 1),        // first SYN sent in clear while negotiating
            new("10.74.1.30", "10.74.1.20", 5985, "synack", 2),     // responder accepted in clear
            new("10.74.1.20", "10.74.1.30", 5985, "tcp", 3),        // clear segments after the handshake
            new("10.74.1.20", "10.74.1.30", 445, "tcp", 4), new("10.74.1.30", "10.74.1.20", 445, "synack", 4),
            new("10.74.1.50", "10.74.1.30", 0, "esp", 99)];
        Assert.Equal((12, 5, 1), Wire.Count(b, "10.74.1.20", "10.74.1.30", 5985));
        Assert.Equal((0, 0, 0), Wire.Count(b, "10.74.1.40", "10.74.1.30", 5985));
    }

    [Fact]
    public async Task Probe_with_capture_starts_and_stops_on_responders_and_attaches_counts() {
        var repo = new PlanRepository(folder);
        var saved = await repo.Save(null, f.Plan, f.Snapshot, PlanRules.Assess(f.Plan, f.Snapshot), "t");
        var runner = new FakeRunner();
        var probe = JsonSerializer.Serialize(new[] {
            new { initiator = "app1", initiatorId = f.App1.Id, responder = "db1", responderId = f.Db1.Id, tcp = true, cipher = (string?)"AES256", integrity = (string?)"SHA256", auth = (string?)"Kerberos", error = (string?)null },
            new { initiator = "client-no", initiatorId = f.ClientNo.Id, responder = "db1", responderId = f.Db1.Id, tcp = false, cipher = (string?)null, integrity = (string?)null, auth = (string?)null, error = (string?)null },
            new { initiator = "fs1", initiatorId = f.Fs1.Id, responder = "ext1", responderId = f.Ext1.Id, tcp = false, cipher = (string?)null, integrity = (string?)null, auth = (string?)null, error = (string?)null } });
        var stop = JsonSerializer.Serialize(new[] {
            new { host = "db1", ok = true, error = (string?)null, bytes = 123456L, sha256 = "ab", buckets = new[] {
                new { source = "10.74.1.20", destination = "10.74.1.30", port = 0, kind = "esp", count = 8 },
                new { source = "10.74.1.20", destination = "10.74.1.30", port = 5985, kind = "syn", count = 1 },
                new { source = "10.74.1.50", destination = "10.74.1.30", port = 5985, kind = "syn", count = 3 } } } }, Json.Settings);
        runner.Reply = s => s.Contains("netsh.exe trace start") ? (true, "[{\"host\":\"db1\",\"ok\":true}]") : s.Contains("netsh.exe trace stop") ? (true, stop) : (true, probe);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Deployment:Enabled"] = "true", ["Deployment:ProbePort"] = "5985" }).Build();
        var d = new Deployment(config, repo, runner, NullLogger<Deployment>.Instance);
        var r = await d.Probe(new(saved.Id, 1, "probe") { Capture = true }, "op");
        Assert.Equal(3, runner.Scripts.Count);
        Assert.Contains("netsh.exe trace start", runner.Scripts[0]);
        Assert.Contains("\"db1.lab.ipsec.test\"", runner.Scripts[0]);
        Assert.DoesNotContain("ext1", runner.Scripts[0]); // outside the domain: never captured
        Assert.Contains("Remove-Item", runner.Scripts[2]); // capture files are deleted on the host
        var appDb = r.Single(x => x.Initiator == "app1");
        Assert.Equal(new WireObservation(8, 0, 1, true), appDb.Wire); // the dropped clear SYN is an attempt, not acceptance
        var clientDb = r.Single(x => x.Initiator == "client-no");
        Assert.Equal(new WireObservation(0, 0, 3, true), clientDb.Wire); // blocked: clear SYNs arrive, never accepted
        Assert.Null(r.Single(x => x.Responder == "ext1").Wire);
    }

    [Fact]
    public async Task Capture_is_stopped_even_when_the_probe_fails() {
        var repo = new PlanRepository(folder);
        var saved = await repo.Save(null, f.Plan, f.Snapshot, PlanRules.Assess(f.Plan, f.Snapshot), "t");
        var runner = new FakeRunner { Reply = s => s.Contains("netsh.exe trace start") ? (true, "[{\"host\":\"db1\",\"ok\":true}]") : s.Contains("netsh.exe trace stop") ? (true, "[]") : (false, "probe exploded") };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Deployment:Enabled"] = "true" }).Build();
        var d = new Deployment(config, repo, runner, NullLogger<Deployment>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => d.Probe(new(saved.Id, 1, "probe") { Capture = true }, "op"));
        Assert.Contains(runner.Scripts, s => s.Contains("netsh.exe trace stop"));
    }

    sealed class FakeRunner : IScriptRunner {
        public readonly List<string> Scripts = [];
        public Func<string, (bool, string)> Reply = _ => (true, "[]");
        public Task<(bool Ok, string Output)> Run(string script, TimeSpan timeout) { Scripts.Add(script); return Task.FromResult(Reply(script)); }
    }
}

public class RequestBindingTests {
    // The API binds requests with the same options; the lab regression posts these exact shapes.
    [Theory]
    [InlineData("{\"planId\":\"p\",\"version\":1,\"action\":\"refresh\"}", false)]
    [InlineData("{\"planId\":\"p\",\"version\":1,\"action\":\"probe\",\"capture\":true}", true)]
    [InlineData("{\"planId\":\"p\",\"version\":1,\"action\":\"probe\",\"computerIds\":[\"a\"],\"capture\":false}", false)]
    public void HostRequest_binds_with_or_without_capture(string json, bool capture) {
        var api = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
        var r = System.Text.Json.JsonSerializer.Deserialize<HostRequest>(json, api)!;
        Assert.Equal("p", r.PlanId); Assert.Equal(capture, r.Capture);
    }
}

public class ScriptRunnerTests {
    // WinRM reconnect notices are host output, not the warning stream: they reach stdout even with $WarningPreference silenced.
    [Fact]
    public void Json_is_read_from_the_first_line_that_starts_it() {
        var noisy = "WARNING: The network connection to app1.lab.ipsec.test has been interrupted. Attempting to reconnect for up to 4 \r\nminutes...\nWARNING: Attempting to reconnect to app1.lab.ipsec.test ...\n[{\"reachable\":true}]";
        Assert.Equal("[{\"reachable\":true}]", PowerShellRunner.JsonPart(noisy));
        Assert.Equal("{\"a\":1}", PowerShellRunner.JsonPart("{\"a\":1}"));
        Assert.Equal("no json here", PowerShellRunner.JsonPart("no json here"));
    }
    [Fact]
    public void Warnings_and_information_are_kept_out_of_the_json_output() {
        var p = PowerShellRunner.Prepare("ConvertTo-Json @(1)");
        Assert.StartsWith("$WarningPreference='SilentlyContinue'", p);
        Assert.Contains("$InformationPreference='SilentlyContinue'", p);
        Assert.EndsWith("ConvertTo-Json @(1)", p);
    }
}
