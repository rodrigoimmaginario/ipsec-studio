using System.Text.Json;
using IpsecStudio;
using Xunit;

namespace IpsecStudio.Tests;

// tests/scenarios/lab-profiles.json is the single source of regression scenarios: the unit suite asserts the simulator
// against each expectation here, and the lab routine applies the same scenarios to real hosts and compares probes.
public class ScenarioTests {
    public sealed record WaveSpec(string Name, Dictionary<string, string[]> Members, string[][] Expect);
    public sealed record Scenario(string Name, Dictionary<string, string[]> Membership, string[] Exemptions, string[][] Expect, WaveSpec[]? Waves);
    public sealed record ScenarioFile(Scenario[] Scenarios, Dictionary<string, string[]> BaselineMembership);
    static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "scenarios", "lab-profiles.json");
    public static ScenarioFile Load() => JsonSerializer.Deserialize<ScenarioFile>(File.ReadAllText(Path), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    public static IEnumerable<object[]> Names() => Load().Scenarios.Select(s => new object[] { s.Name });

    sealed class Bound {
        public readonly LabFixture F = new(); public readonly Dictionary<string, DirectoryObject> Groups; public readonly Dictionary<string, string> Hosts;
        public Bound() {
            Groups = new() { ["IPSEC-Exclusion"] = F.Exclusion, ["IPSEC-Boundary"] = F.Boundary, ["IPSEC-NoPolicy"] = F.NoPolicy, ["IPSEC-SQL-Clients"] = F.SqlClients, ["IPSEC-File-Clients"] = F.FileClients };
            Hosts = new() { ["app1"] = F.App1.Id, ["db1"] = F.Db1.Id, ["fs1"] = F.Fs1.Id, ["client-no"] = F.ClientNo.Id, ["admin1"] = F.Admin1.Id, ["dc1"] = F.Dc1.Id, ["ext1"] = F.Ext1.Id };
        }
        // Scenario membership lists computers only; nested groups already in the fixture are preserved.
        public InventorySnapshot Apply(Dictionary<string, string[]> membership) {
            var s = F.Snapshot;
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
        Assert.Equal(["baseline", "quarantine", "boundary", "exempt-external", "waves"], file.Scenarios.Select(s => s.Name));
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
        var plan = b.F.Plan with { Waves = waves, Exemptions = scenario.Exemptions.Select(a => new ExemptAddress { Id = LabFixture.Id(), Name = "exempt " + a, Address = a, Reason = "scenario", Owner = "lab" }).ToArray() };
        void Check(InventorySnapshot s, string[][] expect, string stage) {
            var a = PlanRules.Assess(plan, s);
            foreach (var e in expect) Assert.True(e[2] == Simulator.Evaluate(plan, s, a, b.Hosts[e[0]], b.Hosts[e[1]]).Outcome, $"{name}/{stage}: {e[0]} -> {e[1]} expected {e[2]}");
        }
        Check(snapshot, scenario.Expect, "before waves");
        for (var i = 0; i < waves.Length; i++) Check(PlanRules.AfterWaves(plan, snapshot, i + 1), scenario.Waves![i].Expect, "after wave " + (i + 1));
    }
}
