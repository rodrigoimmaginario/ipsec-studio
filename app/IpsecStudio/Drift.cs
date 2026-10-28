using System.Text.Json;

namespace IpsecStudio;

// What the directory holds for one Studio GPO, as read by the drift script (names/SIDs only, no free text interpreted).
public sealed record DriftRuleReading(string Name, string Inbound, string Outbound, bool RequireAuthorization, string RemoteAddress);
public sealed record DriftGpoReading(string Name, bool Exists, string? Marker, int Version, bool Linked, DriftRuleReading[] Rules, string AuthorizationSddl, string Exemptions, string[] ApplyGroups, string[] DenySids) { public string[] Errors { get; init; } = []; }
public sealed record DriftExternalReading(string Name, int RuleCount, string[] Links);
public sealed record DriftReading(DriftGpoReading[] Gpos, DriftExternalReading[] External);
public sealed record DriftGpoReport(string Name, bool Exists, string[] Differences);
public sealed record DriftReport(bool Clean, DriftGpoReport[] Gpos, DriftExternalReading[] External, DateTimeOffset Utc);

public static class Drift {
    // Compares the compiled intent with the directory reading. Any difference or any external IPsec GPO makes the report dirty.
    public static DriftReport Compare(Compilation c, DriftReading reading) {
        var gpos = new List<DriftGpoReport>();
        foreach (var g in c.Gpos) {
            var r = reading.Gpos.FirstOrDefault(x => x.Name == g.Name);
            var diff = new List<string>();
            if (r is null || !r.Exists) { gpos.Add(new(g.Name, false, [L.T("drift.missing")])); continue; }
            if (r.Marker != c.Marker) diff.Add(L.T("drift.marker", c.Marker, r.Marker ?? ""));
            foreach (var e in r.Errors ?? []) diff.Add(L.T("drift.readError", e));
            if (!r.Linked) diff.Add(L.T("drift.unlinked"));
            var main = r.Rules.FirstOrDefault(x => x.Name == "IpsecStudio-Main");
            if (main is null) diff.Add(L.T("drift.ruleMissing", "IpsecStudio-Main"));
            else {
                if (!main.Inbound.Equals(g.Inbound, StringComparison.OrdinalIgnoreCase)) diff.Add(L.T("drift.field", "IpsecStudio-Main", "inbound", g.Inbound, main.Inbound));
                if (!main.Outbound.Equals(g.Outbound, StringComparison.OrdinalIgnoreCase)) diff.Add(L.T("drift.field", "IpsecStudio-Main", "outbound", g.Outbound, main.Outbound));
                if (main.RequireAuthorization != g.RequireAuthorization) diff.Add(L.T("drift.field", "IpsecStudio-Main", "requireAuthorization", g.RequireAuthorization, main.RequireAuthorization));
            }
            var exempt = r.Rules.FirstOrDefault(x => x.Name == "IpsecStudio-Exempt");
            if (c.ExemptAddresses.Length > 0 && exempt is null) diff.Add(L.T("drift.ruleMissing", "IpsecStudio-Exempt"));
            else if (exempt is not null) {
                var found = exempt.RemoteAddress.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Order(StringComparer.Ordinal);
                if (!found.SequenceEqual(c.ExemptAddresses.Order(StringComparer.Ordinal))) diff.Add(L.T("drift.field", "IpsecStudio-Exempt", "remoteAddress", string.Join(",", c.ExemptAddresses), exempt.RemoteAddress));
            }
            foreach (var extra in r.Rules.Where(x => x.Name is not ("IpsecStudio-Main" or "IpsecStudio-Exempt"))) diff.Add(L.T("drift.ruleExtra", extra.Name));
            // Windows reports an absent authorization list as "NotConfigured"; the compiler expresses it as null.
            var foundAuth = Normalize(r.AuthorizationSddl);
            if ((g.AuthorizationSddl ?? "") != foundAuth) diff.Add(L.T("drift.field", g.Name, "authorizationList", g.AuthorizationSddl ?? "-", foundAuth == "" ? "-" : foundAuth));
            var applyExtra = r.ApplyGroups.Where(a => !a.Equals(g.ApplyGroup, StringComparison.OrdinalIgnoreCase) && !a.Equals("Authenticated Users", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (applyExtra.Length > 0 || !r.ApplyGroups.Any(a => a.Equals(g.ApplyGroup, StringComparison.OrdinalIgnoreCase))) diff.Add(L.T("drift.field", g.Name, "apply", g.ApplyGroup, string.Join(",", r.ApplyGroups)));
            if (!r.DenySids.ToHashSet().SetEquals(g.DenyGroupSids)) diff.Add(L.T("drift.field", g.Name, "denyApply", string.Join(",", g.DenyGroupSids), string.Join(",", r.DenySids)));
            gpos.Add(new(g.Name, true, diff.ToArray()));
        }
        var clean = gpos.All(x => x.Exists && x.Differences.Length == 0) && reading.External.Length == 0;
        return new(clean, gpos.ToArray(), reading.External, DateTimeOffset.UtcNow);
    }
    static string Normalize(string? value) => value is null || value.Equals("NotConfigured", StringComparison.OrdinalIgnoreCase) ? "" : value.Trim();
    public static DriftReading Parse(string json) { var r = JsonSerializer.Deserialize<DriftReading>(json, Json.Settings) ?? new([], []); return r with { Gpos = r.Gpos ?? [], External = r.External ?? [] }; }
}
