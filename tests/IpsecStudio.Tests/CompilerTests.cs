using IpsecStudio;
using Xunit;

namespace IpsecStudio.Tests;

public class CompilerTests {
    readonly LabFixture f = new();
    const string DomainSid = LabFixture.DomainSid;

    [Fact]
    public void One_gpo_per_profile_with_deny_apply_and_authorization_lists() {
        var c = Compiler.Compile(f.Saved());
        Assert.Equal(["base", "restricted", "restricted", "boundary", "management", "quarantine"], c.Gpos.Select(g => g.Profile));
        var b = c.Gpos[0];
        Assert.Equal(DomainSid + "-515", b.ApplyGroupSid);
        Assert.True(b.DenyGroupSids.ToHashSet().SetEquals([f.SqlServers.Sid!, f.FileServers.Sid!, f.Boundary.Sid!, f.AdminClients.Sid!, f.NoPolicy.Sid!, f.Exclusion.Sid!]));
        Assert.Equal($"O:LSD:(D;;CC;;;{f.Exclusion.Sid})(A;;CC;;;{DomainSid}-515)", b.AuthorizationSddl);
        Assert.Equal(("Require", "Request"), (b.Inbound, b.Outbound)); Assert.True(b.RequireAuthorization);
        var s = c.Gpos.Single(g => g.ApplyGroup == "IPSEC-SQL-Servers");
        Assert.True(s.Encryption); Assert.Equal(("Require", "Request"), (s.Inbound, s.Outbound)); // no outbound Require by default (lab 29/10)
        Assert.Equal($"O:LSD:(D;;CC;;;{f.Exclusion.Sid})(A;;CC;;;{f.SqlServers.Sid})(A;;CC;;;{f.SqlClients.Sid})(A;;CC;;;{f.AdminClients.Sid})", s.AuthorizationSddl);
        Assert.Equal([f.NoPolicy.Sid!, f.Exclusion.Sid!], s.DenyGroupSids);
        var bd = c.Gpos.Single(g => g.Profile == "boundary"); Assert.Equal("Request", bd.Inbound); Assert.False(bd.RequireAuthorization); Assert.Null(bd.AuthorizationSddl);
        Assert.Equal($"O:LSD:(A;;CC;;;{DomainSid}-515)", c.Gpos.Single(g => g.Profile == "management").AuthorizationSddl);
        var q = c.Gpos.Single(g => g.Profile == "quarantine");
        Assert.Equal(f.Exclusion.Sid, q.ApplyGroupSid); Assert.Equal($"O:LSD:(A;;CC;;;{f.AdminClients.Sid})", q.AuthorizationSddl);
        Assert.All(c.Gpos.Where(g => g.Profile != "quarantine"), g => Assert.Contains(f.Exclusion.Sid!, g.DenyGroupSids));
        Assert.Equal(["10.74.1.10"], c.ExemptAddresses);
        Assert.Contains("\"exemptAddresses\":[\"10.74.1.10\"]", c.ApplyScript);
        Assert.Contains("\"secureSubnets\":[\"10.74.1.0/24\"]", c.ApplyScript);
    }

    [Fact]
    public void Deterministic_hash_marker_and_native_ipsec_only() {
        var saved = f.Saved(version: 3);
        var c1 = Compiler.Compile(saved); var c2 = Compiler.Compile(saved);
        Assert.Equal(c1.Sha256, c2.Sha256); Assert.Equal(64, c1.Sha256.Length);
        Assert.NotEqual(c1.Sha256, Compiler.Compile(saved with { Version = 4 }).Sha256);
        Assert.Equal($"IpsecStudio plan {saved.Id} v3", c1.Marker);
        Assert.Contains(c1.Marker, c1.ApplyScript); Assert.Contains("Description -ne $spec.marker", c1.RollbackScript);
        Assert.Contains("-RemoteMachineTransportAuthorizationList", c1.ApplyScript);
        Assert.Contains("Deny-Apply", c1.ApplyScript);
        Assert.Contains("-InboundSecurity None -OutboundSecurity None", c1.ApplyScript);
        Assert.DoesNotContain("New-NetFirewallRule", c1.ApplyScript);
        // Named sets referenced by the rule must not be created as -Default: that renames them and leaves the rule in error (lab finding).
        Assert.Contains("-Name 'IpsecStudio-P1' -Proposal $auth | Out-Null", c1.ApplyScript);
        Assert.Contains("-Name 'IpsecStudio-QM' -Proposal $proposals | Out-Null", c1.ApplyScript);
        Assert.Contains("Remove-GPO -Guid", c1.RollbackScript); Assert.DoesNotContain("New-GPO", c1.RollbackScript);
        var lines = c1.ApplyScript.Split('\n'); var specLine = lines.Single(l => l.StartsWith("{\"domain\""));
        Assert.Equal(1, lines.Count(l => l.Contains("IPSEC-SQL-Servers"))); Assert.Contains("IPSEC-SQL-Servers", specLine);
    }

    [Fact]
    public void Optional_profiles_collapse_cleanly() {
        var minimal = new PlanInput { Name = "Min", InventoryId = f.Snapshot.Id };
        var c = Compiler.Compile(f.Saved(minimal));
        Assert.Single(c.Gpos); Assert.Empty(c.Gpos[0].DenyGroupSids);
        Assert.Equal($"O:LSD:(A;;CC;;;{DomainSid}-515)", c.Gpos[0].AuthorizationSddl);
        Assert.Equal(7, PlanRules.Assess(minimal, f.Snapshot).Hosts.Count(h => h.Profile == "base"));
        var noMgmt = f.Plan with { Settings = f.Plan.Settings with { ManagementGroupId = null } };
        Assert.Equal("O:LSD:", Compiler.Compile(f.Saved(noMgmt, f.With(f.Exclusion with { MemberIds = [f.ClientNo.Id] }))).Gpos.Single(g => g.Profile == "quarantine").AuthorizationSddl);
    }

    [Fact]
    public void Rejects_names_that_cannot_become_a_gpo_name_or_repeat() {
        Assert.Throws<ValidationException>(() => Compiler.Compile(f.Saved(f.Plan with { Restricted = [f.Sql with { Name = "***" }] })));
        Assert.Throws<ValidationException>(() => Compiler.Compile(f.Saved(f.Plan with { Restricted = [f.Sql, f.Files with { Name = f.Sql.Name, ProtectedGroupId = f.FileServers.Id }] })));
        Assert.Throws<ValidationException>(() => Compiler.Compile(f.Saved(f.Plan with { Restricted = [f.Sql with { Name = "Banco de dados" }, f.Files with { Name = "Banco-de dados!" }] })));
    }
}
