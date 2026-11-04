using IpsecStudio;
using Xunit;

namespace IpsecStudio.Tests;

// Waves: ordered membership additions to profile groups, applied by the Studio with the operator's identity.
public class WaveTests {
    readonly LabFixture f = new();
    PlanInput WithWaves() {
        // Start with app1 outside both access groups; wave 1 grants SQL, wave 2 grants files.
        var sqlClients = f.SqlClients with { MemberIds = [f.Admin1.Id] };
        var fileClients = f.FileClients with { MemberIds = [f.AdminClients.Id] };
        snapshot = f.Snapshot with { Objects = f.Snapshot.Objects.Select(o => o.Id == sqlClients.Id ? sqlClients : o.Id == fileClients.Id ? fileClients : o).ToArray() };
        return f.Plan with { Waves = [
            new() { Id = LabFixture.Id(), Name = "Piloto SQL", Members = [new() { GroupId = f.SqlClients.Id, ComputerIds = [f.App1.Id] }] },
            new() { Id = LabFixture.Id(), Name = "Arquivos", Members = [new() { GroupId = f.FileClients.Id, ComputerIds = [f.App1.Id] }] }] };
    }
    InventorySnapshot snapshot = null!;

    [Fact]
    public void Membership_after_each_wave_changes_the_simulated_outcome() {
        var plan = WithWaves();
        string After(int wave, string from, string to) { var s = PlanRules.AfterWaves(plan, snapshot, wave); return Simulator.Evaluate(plan, s, PlanRules.Assess(plan, s), from, to).Outcome; }
        Assert.Equal("blocked", After(0, f.App1.Id, f.Db1.Id));
        Assert.Equal("blocked", After(0, f.App1.Id, f.Fs1.Id));
        Assert.Equal("encrypted", After(1, f.App1.Id, f.Db1.Id));
        Assert.Equal("blocked", After(1, f.App1.Id, f.Fs1.Id));
        Assert.Equal("encrypted", After(2, f.App1.Id, f.Db1.Id));
        Assert.Equal("authenticated", After(2, f.App1.Id, f.Fs1.Id));
        Assert.Equal(PlanRules.AfterWaves(plan, snapshot, 99).Objects.Length, snapshot.Objects.Length);
    }

    [Fact]
    public void Waves_are_validated_against_inventory_and_profile_groups() {
        var plan = WithWaves();
        Assert.Throws<ValidationException>(() => PlanRules.Assess(plan with { Waves = [plan.Waves[0] with { Members = [new() { GroupId = LabFixture.Id(), ComputerIds = [f.App1.Id] }] }] }, snapshot));
        Assert.Throws<ValidationException>(() => PlanRules.Assess(plan with { Waves = [plan.Waves[0] with { Members = [new() { GroupId = f.SqlClients.Id, ComputerIds = [LabFixture.Id()] }] }] }, snapshot));
        Assert.Throws<ValidationException>(() => PlanRules.Assess(plan with { Waves = [plan.Waves[0] with { Members = [new() { GroupId = f.DomainComputers.Id, ComputerIds = [f.App1.Id] }] }] }, snapshot));
        Assert.Throws<ValidationException>(() => PlanRules.Assess(plan with { Waves = [plan.Waves[0], plan.Waves[0]] }, snapshot));
        var a = PlanRules.Assess(plan, snapshot);
        Assert.Contains(a.Warnings, w => w.Contains("Piloto SQL"));
    }

    [Fact]
    public void Wave_scripts_use_sids_only_and_rollback_removes_the_same_members() {
        var plan = WithWaves();
        var saved = new SavedPlan(LabFixture.Id(), 1, plan.Name, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "t", plan, snapshot, PlanRules.Assess(plan, snapshot));
        var w = Compiler.CompileWave(saved, plan.Waves[0].Id);
        Assert.Contains($"\"g\":\"{f.SqlClients.Sid}\",\"c\":\"{f.App1.Sid}\"", w.ApplyScript);
        Assert.Contains("Add-ADGroupMember -Identity $p.g -Members $p.c", w.ApplyScript);
        Assert.Contains("Remove-ADGroupMember -Identity $e.g -Members $e.c", w.RollbackScript);
        Assert.Contains("$owned=@($journal | Where-Object { $_.state -eq 'added' })", w.RollbackScript);
        Assert.DoesNotContain("app1", w.ApplyScript.Split('\n').Single(l => l.Contains("Add-ADGroupMember")));
        Assert.Equal(64, w.Sha256.Length);
        Assert.Equal([f.App1.Id], w.ComputerIds);
        Assert.Throws<ValidationException>(() => Compiler.CompileWave(saved, LabFixture.Id()));
    }

    [Fact]
    public void Group_preparation_script_creates_only_validated_names_in_the_chosen_container() {
        var plan = f.Plan with { NewGroups = [new() { Id = LabFixture.Id(), Name = "IPSEC-Web-Servers", Description = "Servidores web" }], Settings = f.Plan.Settings with { GroupContainer = "OU=Groups,DC=lab,DC=ipsec,DC=test" } };
        var saved = new SavedPlan(LabFixture.Id(), 1, plan.Name, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "t", plan, f.Snapshot, PlanRules.Assess(plan, f.Snapshot));
        var g = Compiler.CompileGroups(saved);
        Assert.Contains("New-ADGroup -Name $g.name -GroupScope Global -GroupCategory Security -Path $spec.container", g.ApplyScript);
        Assert.Contains("\"container\":\"OU=Groups,DC=lab,DC=ipsec,DC=test\"", g.ApplyScript);
        var quoted = plan with { Settings=plan.Settings with { GroupContainer="OU=Equipe d'Operacao,DC=lab,DC=ipsec,DC=test" } };
        var quotedScript=Compiler.CompileGroups(f.Saved(quoted)).ApplyScript;
        Assert.Contains("\\u0027",quotedScript);
        Assert.DoesNotContain("-Path 'OU=Equipe d'Operacao",quotedScript);
        Assert.Throws<ValidationException>(() => PlanRules.Assess(plan with { NewGroups = [plan.NewGroups[0] with { Name = "bad'name" }] }, f.Snapshot));
        Assert.Throws<ValidationException>(() => PlanRules.Assess(plan with { NewGroups = [plan.NewGroups[0] with { Name = f.SqlServers.Name }] }, f.Snapshot));
        Assert.Throws<ValidationException>(() => PlanRules.Assess(plan with { Settings = plan.Settings with { GroupContainer = "not a dn" } }, f.Snapshot));
    }
}
