using System.Text.Json;
using IpsecStudio;
using Xunit;

namespace IpsecStudio.Tests;

// tests/scenarios/lab-profiles.json is the single source of regression scenarios: the unit suite asserts the simulator
// against each expectation here, and the lab routine applies the same scenarios to real hosts and compares probes.
public class ScenarioTests {
    public sealed record WaveSpec(string Name, Dictionary<string, string[]> Members, string[][] Expect);
    // ExternalHosts: [name, IPv4] of devices outside the directory (e.g. the prn1 printer stand-in).
    public sealed record Scenario(string Name, Dictionary<string, string[]> Membership, string[] Exemptions, string[][] Expect, WaveSpec[]? Waves, string[][]? ExternalHosts = null);
    public sealed record ScenarioFile(Scenario[] Scenarios, Dictionary<string, string[]> BaselineMembership);
    static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "scenarios", "lab-profiles.json");
    public static ScenarioFile Load() => JsonSerializer.Deserialize<ScenarioFile>(File.ReadAllText(Path), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    public static IEnumerable<object[]> Names() => Load().Scenarios.Select(s => new object[] { s.Name });

    sealed class Bound {
        public readonly LabFixture F = new(); public readonly Dictionary<string, DirectoryObject> Groups; public readonly Dictionary<string, string> Hosts;
        readonly InventorySnapshot snapshot;
        public Bound() {
            Groups = new() { ["IPSEC-Exclusion"] = F.Exclusion, ["IPSEC-Boundary"] = F.Boundary, ["IPSEC-NoPolicy"] = F.NoPolicy, ["IPSEC-SQL-Clients"] = F.SqlClients, ["IPSEC-File-Clients"] = F.FileClients };
            // CV4.DS5 lab fixtures: Windows 10/11 clients, a second DC and the WSUS server (the fixture's "wsus", same address).
            var rid = 9000;
            DirectoryObject Computer(string name, string ip, string kind) => new() { Id = LabFixture.Id(), Name = name, Kind = kind, Ip = ip, Sid = LabFixture.DomainSid + "-" + ++rid, Dn = $"CN={name},OU=IpsecProof,DC=lab,DC=ipsec,DC=test" };
            var win10 = Computer("win10", "10.74.1.51", "workstation"); var win11 = Computer("win11", "10.74.1.52", "workstation"); var dc2 = Computer("dc2", "10.74.1.11", "dc");
            // The fixture's "legacy" computer used 10.74.1.90, now the printer stand-in's address.
            snapshot = F.Snapshot with { Objects = [.. F.Snapshot.Objects.Select(o => o.Id == F.Legacy.Id ? o with { Ip = "10.74.1.99" } : o.Id == F.DomainComputers.Id ? o with { MemberIds = [.. o.MemberIds, win10.Id, win11.Id] } : o), win10, win11, dc2] };
            Hosts = new() { ["app1"] = F.App1.Id, ["db1"] = F.Db1.Id, ["fs1"] = F.Fs1.Id, ["client-no"] = F.ClientNo.Id, ["admin1"] = F.Admin1.Id, ["dc1"] = F.Dc1.Id, ["ext1"] = F.Ext1.Id,
                ["win10"] = win10.Id, ["win11"] = win11.Id, ["dc2"] = dc2.Id, ["wsus1"] = F.Wsus.Id };
        }
        // Scenario membership lists computers only; nested groups already in the fixture are preserved.
        public InventorySnapshot Apply(Dictionary<string, string[]> membership) {
            var s = snapshot;
            foreach (var (group, members) in membership) {
                var g = Groups[group]; var nested = g.MemberIds.Where(id => !Hosts.ContainsValue(id)).ToArray();
                s = s with { Objects = s.Objects.Select(o => o.Id == g.Id ? o with { MemberIds = nested.Concat(members.Select(m => Hosts[m])).ToArray() } : o).ToArray() };
            }
            return s;
        }
    }

    [Fact]
    public void Scenario_file_has_the_lab_scenarios_with_expectations_and_a_baseline_membership() {
        var file = Load();
        Assert.Equal(["baseline", "quarantine", "boundary", "exempt-external", "waves", "fixtures"], file.Scenarios.Select(s => s.Name));
        Assert.All(file.Scenarios, s => Assert.NotEmpty(s.Expect));
        Assert.Contains("IPSEC-SQL-Clients", file.BaselineMembership.Keys);
        Assert.Equal(2, file.Scenarios.Single(s => s.Name == "waves").Waves!.Length);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Simulator_matches_every_expectation_including_per_wave(string name) {
        var b = new Bound();
        var scenario = Load().Scenarios.Single(s => s.Name == name);
        var snapshot = b.Apply(scenario.Membership);
        var waves = (scenario.Waves ?? []).Select(w => new Wave { Id = LabFixture.Id(), Name = w.Name, Members = w.Members.Select(m => new WaveMember { GroupId = b.Groups[m.Key].Id, ComputerIds = m.Value.Select(c => b.Hosts[c]).ToArray() }).ToArray() }).ToArray();
        var externals = (scenario.ExternalHosts ?? []).Select(x => new ExternalHost { Id = LabFixture.Id(), Name = x[0], Address = x[1] }).ToArray();
        foreach (var x in externals) b.Hosts[x.Name] = x.Id;
        var plan = b.F.Plan with { Waves = waves, ExternalHosts = [.. b.F.Plan.ExternalHosts, .. externals], Exemptions = scenario.Exemptions.Select(a => new ExemptAddress { Id = LabFixture.Id(), Name = "exempt " + a, Address = a, Reason = "scenario", Owner = "lab" }).ToArray() };
        var wrong = new List<string>();
        void Check(InventorySnapshot s, string[][] expect, string stage) {
            var a = PlanRules.Assess(plan, s);
            foreach (var e in expect) { var got = Simulator.Evaluate(plan, s, a, b.Hosts[e[0]], b.Hosts[e[1]]).Outcome; if (e[2] != got) wrong.Add($"{name}/{stage}: {e[0]} -> {e[1]} expected {e[2]}, simulated {got}"); }
        }
        Check(snapshot, scenario.Expect, "before waves");
        for (var i = 0; i < waves.Length; i++) Check(PlanRules.AfterWaves(plan, snapshot, i + 1), scenario.Waves![i].Expect, "after wave " + (i + 1));
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }
}
