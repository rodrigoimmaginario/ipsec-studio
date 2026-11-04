using System.Text.Json;
using System.Text.Json.Nodes;
namespace IpsecStudio;

public sealed record PolicyCheck(bool Healthy, string[] Differences);
public static class EffectivePolicy {
    public static PolicyCheck Check(SavedPlan plan, InventorySnapshot membership, HostResult host, bool expectAbsent = false) {
        if (!host.Reachable) return new(false, [L.T("verify.unreachable")]);
        try {
            using var doc = JsonDocument.Parse(host.Output);
            var root = doc.RootElement;
            var profile = PlanRules.Assess(plan.Input, membership).Hosts.FirstOrDefault(h => (h.Name + "." + membership.Domain).Equals(host.Host, StringComparison.OrdinalIgnoreCase));
            if (profile is null) return new(false, [L.T("verify.host")]);
            if (!root.TryGetProperty("computer",out var computer) || computer.ValueKind!=JsonValueKind.String || !profile.Name.Equals(computer.GetString(),StringComparison.OrdinalIgnoreCase)) return new(false,[L.T("verify.identity")]);
            var rows = root.GetProperty("studioRules").EnumerateArray().ToArray();
            if (expectAbsent || profile.Profile is "none" or "noPolicy") {
                var absence = new List<string>();
                if (rows.Length > 0) absence.Add(L.T("verify.unexpected"));
                if (root.TryGetProperty("outside",out var external) && external.ValueKind == JsonValueKind.False) absence.Add(L.T("verify.outside"));
                return new(absence.Count == 0,absence.ToArray());
            }
            var compilation = Compiler.Compile(plan with { Inventory = membership });
            var gpo = compilation.Gpos.Single(g => g.ComputerIds.Contains(profile.ComputerId));
            string Text(JsonElement row, string key) => row.TryGetProperty(key, out var value) ? value.ToString() : "";
            var rules = rows.Select(r => new DriftRuleReading(Text(r,"name"),Text(r,"inbound"),Text(r,"outbound"),r.TryGetProperty("requireAuthorization",out var auth) && auth.ValueKind == JsonValueKind.True,Text(r,"remoteAddress")) { Enabled=Text(r,"enabled").Equals("True",StringComparison.OrdinalIgnoreCase), LocalAddress=Text(r,"localAddress"), Protocol=Text(r,"protocol"), LocalPort=Text(r,"localPort"), RemotePort=Text(r,"remotePort"), Mode=Text(r,"mode"), Profile=Text(r,"profile"), Phase1AuthSet=Text(r,"phase1AuthSet"), QuickModeCryptoSet=Text(r,"quickModeCryptoSet") }).ToArray();
            var crypto = root.TryGetProperty("crypto",out var k) ? k.Deserialize<DriftCryptoReading>(Json.Settings) : null;
            // GPO permissions/links are checked against AD separately. Here compare the effective settings on this host.
            var reading = new DriftGpoReading(gpo.Name,true,compilation.Marker,0,true,rules,Text(root,"authorizationList"),Text(root,"exemptions"),[gpo.ApplyGroup],gpo.DenyGroupSids) { Crypto=crypto };
            var single = compilation with { Gpos=[gpo] };
            var diff = Drift.Compare(single,new([reading],[])).Gpos.Single().Differences.ToList();
            if (rows.Any(r => Text(r,"status") != "OK")) diff.Add(L.T("verify.status"));
            if (rows.Any(r => !Text(r,"source").Contains(gpo.Name,StringComparison.OrdinalIgnoreCase))) diff.Add(L.T("verify.source"));
            if (root.TryGetProperty("outside",out var outside) && outside.ValueKind == JsonValueKind.False) diff.Add(L.T("verify.outside"));
            return new(diff.Count == 0,diff.ToArray());
        } catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or ValidationException) {
            return new(false,[L.T("verify.incomplete")]);
        }
    }
    public static HostResult Enrich(SavedPlan plan, InventorySnapshot membership, HostResult host, bool expectAbsent = false) {
        var check=Check(plan,membership,host,expectAbsent);
        JsonObject output;
        try { output=JsonNode.Parse(host.Output) as JsonObject ?? new(); } catch(JsonException) { output=new() { ["raw"]=host.Output }; }
        output["healthy"]=check.Healthy;
        output["differences"]=JsonSerializer.SerializeToNode(check.Differences);
        return host with { Output=output.ToJsonString(Json.Settings) };
    }
}
