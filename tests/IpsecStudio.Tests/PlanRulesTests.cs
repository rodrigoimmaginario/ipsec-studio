using IpsecStudio;
using Xunit;

namespace IpsecStudio.Tests;

public class PlanRulesTests {
    readonly LabFixture f = new();
    static void Rejects(Func<object> action) => Assert.Throws<ValidationException>(() => action());

    [Fact]
    public void Each_computer_resolves_to_exactly_one_profile() {
        var a = PlanRules.Assess(f.Plan, f.Snapshot);
        string P(DirectoryObject c) => a.Hosts.Single(h => h.ComputerId == c.Id).Profile;
        Assert.Equal("dc", P(f.Dc1)); Assert.Equal("base", P(f.App1)); Assert.Equal("base", P(f.ClientNo));
        Assert.Equal("restricted", P(f.Db1)); Assert.Equal("restricted", P(f.Fs1)); Assert.Equal("management", P(f.Admin1));
        Assert.Equal("boundary", P(f.Wsus)); Assert.Equal("noPolicy", P(f.Legacy));
        Assert.Equal(["10.74.1.10"], a.DomainControllerAddresses);
        Assert.All(a.Hosts, h => Assert.Empty(h.Conflicts));
        Assert.Equal([f.Db1.Id], a.Profiles.Single(p => p.Key == "restricted:" + f.Sql.Id).ComputerIds);
    }

    [Fact]
    public void Conflicting_memberships_are_reported_and_block_compilation() {
        var s = f.With(f.FileServers with { MemberIds = [f.Fs1.Id, f.Db1.Id] });
        var a = PlanRules.Assess(f.Plan, s);
        Assert.Single(a.Hosts.Single(h => h.ComputerId == f.Db1.Id).Conflicts);
        Rejects(() => Compiler.Compile(f.Saved(snapshot: s)));
    }

    [Fact]
    public void Invalid_references_sids_addresses_and_crypto_are_rejected() {
        Rejects(() => PlanRules.Assess(f.Plan with { BoundaryGroupId = LabFixture.Id() }, f.Snapshot));
        Rejects(() => PlanRules.Assess(f.Plan with { Exemptions = [new() { Id = LabFixture.Id(), Name = "x", Address = "10.74.1", Reason = "r", Owner = "o" }] }, f.Snapshot));
        Rejects(() => PlanRules.Assess(f.Plan with { Settings = f.Plan.Settings with { SecureSubnets = ["10.74.0.0/4"] } }, f.Snapshot));
        Rejects(() => PlanRules.Assess(f.Plan with { Settings = f.Plan.Settings with { Crypto = new() { MainModeEncryption = "DES3" } } }, f.Snapshot));
        Rejects(() => PlanRules.Assess(f.Plan, f.With(f.SqlServers with { Sid = "S-1-5-32-544" })));
        Rejects(() => PlanRules.Assess(f.Plan, f.With(f.SqlServers with { Name = "x';Remove-GPO" })));
        Rejects(() => PlanRules.Assess(f.Plan with { BaseMode = "strict" }, f.Snapshot));
        Rejects(() => PlanRules.Assess(f.Plan with { Settings = f.Plan.Settings with { LinkTarget = "OU=Nope,DC=x" } }, f.Snapshot));
        Rejects(() => PlanRules.Assess(f.Plan with { ExternalHosts = [f.Ext1 with { Address = "nope" }] }, f.Snapshot));
    }

    [Fact]
    public void Warnings_name_missing_ips_old_snapshots_and_unexempted_dcs() {
        var old = f.Snapshot with { CapturedAt = DateTimeOffset.UtcNow.AddHours(-1) };
        var a = PlanRules.Assess(f.Plan with { Settings = f.Plan.Settings with { ExemptDomainControllers = false } }, f.With(f.App1 with { Ip = null }) with { CapturedAt = old.CapturedAt });
        Assert.Contains(a.Warnings, w => w.Contains("30 minutos"));
        Assert.Contains(a.Warnings, w => w.StartsWith("app1:"));
        Assert.Contains(a.Warnings, w => w.Contains("DCs não isentos"));
    }

    [Fact]
    public void Nested_membership_cycles_and_unresolved_members_are_bounded_and_warned() {
        var cyc = f.SqlClients with { MemberIds = [f.SqlClients.Id, f.App1.Id], UnresolvedMemberCount = 1 };
        var warnings = new List<string>();
        var ids = PlanRules.ComputersInGroup(cyc.Id, f.With(cyc), warnings);
        Assert.Equal([f.App1.Id], ids);
        Assert.Contains(warnings, w => w.Contains("Ciclo"));
        Assert.Contains(warnings, w => w.Contains("incompleto"));
    }

    [Fact]
    public void Exempting_a_protected_server_address_is_flagged_as_conflict() {
        var plan = f.Plan with { Exemptions = [new() { Id = LabFixture.Id(), Name = "x", Address = "10.74.1.30", Reason = "r", Owner = "o" }] };
        Assert.Single(PlanRules.Assess(plan, f.Snapshot).Hosts.Single(h => h.ComputerId == f.Db1.Id).Conflicts);
    }

    [Fact]
    public void Quarantine_is_the_primary_profile_and_cidr_math_is_correct() {
        var s = f.With(f.Exclusion with { MemberIds = [f.Db1.Id] });
        Assert.Equal("quarantine", PlanRules.Assess(f.Plan, s).Hosts.Single(h => h.ComputerId == f.Db1.Id).Profile);
        Assert.True(PlanRules.InCidr("10.74.1.200", "10.74.1.0/24"));
        Assert.False(PlanRules.InCidr("10.74.2.1", "10.74.1.0/24"));
        Assert.True(PlanRules.ValidCidr("10.0.0.0/8")); Assert.False(PlanRules.ValidCidr("10.0.0.0/33")); Assert.False(PlanRules.ValidIpv4("010.1.1.1"));
    }

    [Fact]
    public void Messages_follow_the_request_language() {
        L.Lang = "en";
        try { Assert.Contains("Invalid base mode", Assert.Throws<ValidationException>(() => PlanRules.Assess(f.Plan with { BaseMode = "strict" }, f.Snapshot)).Message); }
        finally { L.Lang = "pt"; }
        Assert.Contains("Modo da base", Assert.Throws<ValidationException>(() => PlanRules.Assess(f.Plan with { BaseMode = "strict" }, f.Snapshot)).Message);
    }
}
