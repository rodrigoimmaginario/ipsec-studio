using System.Text.Json;
using IpsecStudio;
using Xunit;

namespace IpsecStudio.Tests;

// tests/scenarios/lab-profiles.json is the single source of regression scenarios: the unit suite asserts the simulator
// against each expectation here, and the lab routine applies the same scenarios to real hosts and compares probes.
public class ScenarioTests {
    public sealed record Scenario(string Name, Dictionary<string, string[]> Membership, string[] Exemptions, string[][] Expect);
    public sealed record ScenarioFile(Scenario[] Scenarios);
    static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "scenarios", "lab-profiles.json");
    public static ScenarioFile Load() => JsonSerializer.Deserialize<ScenarioFile>(File.ReadAllText(Path), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    public static IEnumerable<object[]> Names() => Load().Scenarios.Select(s => new object[] { s.Name });

    [Fact]
    public void Scenario_file_has_the_four_lab_scenarios_with_expectations() {
        var names = Load().Scenarios.Select(s => s.Name).ToArray();
        Assert.Equal(["baseline", "quarantine", "boundary", "exempt-external"], names);
        Assert.All(Load().Scenarios, s => Assert.NotEmpty(s.Expect));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Simulator_matches_every_expectation(string name) {
        var f = new LabFixture();
        var scenario = Load().Scenarios.Single(s => s.Name == name);
        var groups = new Dictionary<string, DirectoryObject> { ["IPSEC-Exclusion"] = f.Exclusion, ["IPSEC-Boundary"] = f.Boundary, ["IPSEC-NoPolicy"] = f.NoPolicy };
        var hosts = new Dictionary<string, string> { ["app1"] = f.App1.Id, ["db1"] = f.Db1.Id, ["fs1"] = f.Fs1.Id, ["client-no"] = f.ClientNo.Id, ["admin1"] = f.Admin1.Id, ["dc1"] = f.Dc1.Id, ["ext1"] = f.Ext1.Id };
        var snapshot = f.Snapshot;
        foreach (var (group, members) in scenario.Membership)
            snapshot = snapshot with { Objects = snapshot.Objects.Select(o => o.Id == groups[group].Id ? o with { MemberIds = members.Select(m => hosts[m]).ToArray() } : o).ToArray() };
        var plan = f.Plan with { Exemptions = scenario.Exemptions.Select(a => new ExemptAddress { Id = LabFixture.Id(), Name = "exempt " + a, Address = a, Reason = "scenario", Owner = "lab" }).ToArray() };
        var a = PlanRules.Assess(plan, snapshot);
        foreach (var e in scenario.Expect)
            Assert.True(e[2] == Simulator.Evaluate(plan, snapshot, a, hosts[e[0]], hosts[e[1]]).Outcome, $"{name}: {e[0]} -> {e[1]} expected {e[2]}");
    }
}
