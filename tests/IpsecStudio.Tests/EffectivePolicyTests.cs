using System.Text.Json;
using IpsecStudio;
using Xunit;
namespace IpsecStudio.Tests;

public class EffectivePolicyTests {
    readonly LabFixture f = new();
    public static string Output(Compilation c, GpoSpec g, string computer="app1") => JsonSerializer.Serialize(new {
        computer,
        studioRules = new[] {
            new { localAddress="Any",protocol="Any",localPort="Any",remotePort="Any",mode="Transport",profile="Any",phase1AuthSet="IpsecStudio-P1",quickModeCryptoSet="IpsecStudio-QM",name="IpsecStudio-Main",inbound=g.Inbound,outbound=g.Outbound,requireAuthorization=g.RequireAuthorization,remoteAddress=string.Join(',',c.SecureSubnets.Length == 0 ? ["Any"] : c.SecureSubnets),enabled="True",status="OK",source=g.Name },
            new { localAddress="Any",protocol="Any",localPort="Any",remotePort="Any",mode="Transport",profile="Any",phase1AuthSet="",quickModeCryptoSet="",name="IpsecStudio-Exempt",inbound="None",outbound="None",requireAuthorization=false,remoteAddress=string.Join(',',c.ExemptAddresses),enabled="True",status="OK",source=g.Name }
        }, authorizationList=g.AuthorizationSddl ?? "NotConfigured",exemptions=c.ExemptIcmp ? "Icmp" : "None",crypto=Drift.ExpectedCrypto(c,g),healthy=true
    },Json.Settings);
    [Fact]
    public void Effective_policy_must_match_intent_not_just_report_OK() {
        var saved=f.Saved(); var c=Compiler.Compile(saved); var g=c.Gpos.First();
        var h=new HostResult("app1.lab.ipsec.test",true,Output(c,g));
        Assert.True(EffectivePolicy.Check(saved,f.Snapshot,h).Healthy);
        Assert.False(EffectivePolicy.Check(saved,f.Snapshot,h with {Output=h.Output.Replace("app1","another-host")}).Healthy);
        Assert.False(EffectivePolicy.Check(saved,f.Snapshot,h with {Output="{\"healthy\":true,\"studioRules\":[]}"}).Healthy);
        Assert.True(EffectivePolicy.Check(saved,f.Snapshot,h with {Output="{\"computer\":\"app1\",\"studioRules\":[]}"},expectAbsent:true).Healthy);
        Assert.False(EffectivePolicy.Check(saved,f.Snapshot,h with {Output="{\"computer\":\"app1\",\"studioRules\":[],\"outside\":false}"},expectAbsent:true).Healthy);
        Assert.False(EffectivePolicy.Check(saved,f.Snapshot,h with {Output=h.Output.Replace("\"True\"","\"False\"")}).Healthy);
        Assert.False(EffectivePolicy.Check(saved,f.Snapshot,h with {Output=h.Output.Replace("10.74.1.0/24","192.0.2.0/24")}).Healthy);
    }
    [Fact]
    public void No_policy_is_healthy_only_when_no_Studio_rules_are_present() {
        var p=f.Saved();
        Assert.True(EffectivePolicy.Check(p,f.Snapshot,new("legacy.lab.ipsec.test",true,"{\"computer\":\"legacy\",\"studioRules\":[]}")).Healthy);
        var c=Compiler.Compile(p);
        Assert.False(EffectivePolicy.Check(p,f.Snapshot,new("legacy.lab.ipsec.test",true,Output(c,c.Gpos[0]))).Healthy);
    }
}
