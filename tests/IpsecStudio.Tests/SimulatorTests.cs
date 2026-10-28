using IpsecStudio;
using Xunit;

namespace IpsecStudio.Tests;

public class SimulatorTests {
    readonly LabFixture f = new();

    [Fact]
    public void Access_group_reaches_encrypted_server_and_others_are_blocked() {
        Assert.Equal("encrypted", f.Outcome(f.App1.Id, f.Db1.Id));
        Assert.Equal("encrypted", f.Outcome(f.Admin1.Id, f.Db1.Id));
        Assert.Equal("authenticated", f.Outcome(f.App1.Id, f.Fs1.Id));
        Assert.Equal("blocked", f.Outcome(f.ClientNo.Id, f.Db1.Id));
        Assert.Equal("blocked", f.Outcome(f.Ext1.Id, f.Db1.Id));
        Assert.Equal("blocked", f.Outcome(f.Db1.Id, f.Fs1.Id));
        Assert.Equal("encrypted", f.Outcome(f.Db1.Id, f.App1.Id));
        Assert.Equal("encrypted", f.Outcome(f.Db1.Id, f.ClientNo.Id)); // responder authorizes any domain computer
    }

    [Fact]
    public void Base_boundary_no_policy_external_and_dc_semantics() {
        Assert.Equal("authenticated", f.Outcome(f.ClientNo.Id, f.App1.Id));
        Assert.Equal("blocked", f.Outcome(f.Ext1.Id, f.App1.Id));
        Assert.Equal("clear", f.Outcome(f.App1.Id, f.Ext1.Id));
        Assert.Equal("blocked", f.Outcome(f.Db1.Id, f.Ext1.Id));
        Assert.Equal("clear", f.Outcome(f.Ext1.Id, f.Wsus.Id));
        Assert.Equal("authenticated", f.Outcome(f.App1.Id, f.Wsus.Id));
        Assert.Equal("blocked", f.Outcome(f.Legacy.Id, f.App1.Id));
        Assert.Equal("clear", f.Outcome(f.Legacy.Id, f.Wsus.Id));
        Assert.Equal("clear", f.Outcome(f.App1.Id, f.Dc1.Id));
        Assert.Equal("clear", f.Outcome(f.Ext1.Id, f.Dc1.Id));
        Assert.Equal("blocked", f.Outcome(f.App1.Id, f.Ext1.Id, f.Plan with { BaseMode = "require" }));
        Assert.Equal("clear", f.Outcome(f.Ext1.Id, f.App1.Id, f.Plan with { BaseMode = "request" }));
    }

    [Fact]
    public void Exempt_address_wins_and_secure_subnet_bounds_negotiation() {
        var exempt = f.Plan with { Exemptions = [new() { Id = LabFixture.Id(), Name = "Impressora", Address = "10.74.1.60", Reason = "legado", Owner = "rede" }] };
        Assert.Equal("clear", f.Outcome(f.Ext1.Id, f.Db1.Id, exempt));
        Assert.Equal("clear", f.Outcome(f.Db1.Id, f.Ext1.Id, exempt));
        var far = f.Plan with { ExternalHosts = [f.Ext1 with { Address = "10.74.2.60" }] };
        Assert.Equal("clear", f.Outcome(far.ExternalHosts[0].Id, f.App1.Id, far));
        Assert.Equal("indeterminate", f.Outcome(f.App1.Id, f.App1.Id));
        Assert.Equal("blocked", f.Outcome("10.74.1.99", f.App1.Id));
        Assert.Throws<ValidationException>(() => f.Outcome("nope", f.App1.Id));
    }

    [Fact]
    public void Quarantine_denies_everywhere_except_dcs_exemptions_boundary_and_management() {
        var s = f.With(f.Exclusion with { MemberIds = [f.ClientNo.Id] });
        Assert.Equal("blocked", f.Outcome(f.ClientNo.Id, f.App1.Id, snapshot: s));
        Assert.Equal("blocked", f.Outcome(f.ClientNo.Id, f.Db1.Id, snapshot: s));
        Assert.Equal("blocked", f.Outcome(f.App1.Id, f.ClientNo.Id, snapshot: s));
        Assert.Equal("blocked", f.Outcome(f.Ext1.Id, f.ClientNo.Id, snapshot: s));
        Assert.Equal("authenticated", f.Outcome(f.ClientNo.Id, f.Admin1.Id, snapshot: s));
        Assert.Equal("authenticated", f.Outcome(f.Admin1.Id, f.ClientNo.Id, snapshot: s));
        Assert.Equal("clear", f.Outcome(f.ClientNo.Id, f.Dc1.Id, snapshot: s));
        Assert.Equal("authenticated", f.Outcome(f.ClientNo.Id, f.Wsus.Id, snapshot: s));
        var noMgmt = f.Plan with { Settings = f.Plan.Settings with { ManagementGroupId = null } };
        Assert.Equal("blocked", f.Outcome(f.ClientNo.Id, f.Admin1.Id, noMgmt, s));
        Assert.Equal("blocked", f.Outcome(f.Admin1.Id, f.ClientNo.Id, noMgmt, s));
        Assert.Equal(8 * 7, Simulator.Matrix(f.Plan, s, PlanRules.Assess(f.Plan, s)).Length);
    }

    [Fact]
    public void Reasons_are_localized() {
        var a = PlanRules.Assess(f.Plan, f.Snapshot);
        L.Lang = "en";
        try { Assert.Contains(Simulator.Evaluate(f.Plan, f.Snapshot, a, f.ClientNo.Id, f.Db1.Id).Reasons, r => r.Contains("does not authorize")); }
        finally { L.Lang = "pt"; }
        Assert.Contains(Simulator.Evaluate(f.Plan, f.Snapshot, a, f.ClientNo.Id, f.Db1.Id).Reasons, r => r.Contains("não autoriza"));
    }
}
