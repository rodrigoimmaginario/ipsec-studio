using System.Text.Json;
using IpsecStudio;
namespace IpsecStudio.Tests;

// A completed AD scan explicitly reports each requested GPO absent. An empty object is NOT a successful scan.
internal static class ScriptReplies {
    public static string AbsentGpos(string script) {
        using var spec=JsonDocument.Parse(script.Split('\n').First(l=>l.StartsWith("{\"domain\"")));
        var names=spec.RootElement.GetProperty("gpos").EnumerateArray().Select(v=>v.GetString()!).ToArray();
        return JsonSerializer.Serialize(new DriftReading(names.Select(n=>new DriftGpoReading(n,false,null,0,false,[],"","",[],[])).ToArray(),[]),Json.Settings);
    }
    public static string CleanGpos(Compilation c) => JsonSerializer.Serialize(new DriftReading(c.Gpos.Select(g=>new DriftGpoReading(g.Name,true,c.Marker,1,true,
        [new("IpsecStudio-Main",g.Inbound,g.Outbound,g.RequireAuthorization,c.SecureSubnets.Length==0 ? "Any" : string.Join(',',c.SecureSubnets)),new("IpsecStudio-Exempt","None","None",false,string.Join(',',c.ExemptAddresses))],
        g.AuthorizationSddl??"",c.ExemptIcmp?"Icmp":"None",[g.ApplyGroup],g.DenyGroupSids){Crypto=Drift.ExpectedCrypto(c,g),ApplySids=[g.ApplyGroupSid]}).ToArray(),[]),Json.Settings);
    public static (bool,string) Default(string script) => (true,script.Contains("external=$external") ? AbsentGpos(script) : "{}");
}
