using System.Text.Json;

namespace IpsecStudio;

// What the directory holds for one Studio GPO, as read by the drift script (names/SIDs only, no free text interpreted).
public sealed record DriftRuleReading(string Name, string Inbound, string Outbound, bool RequireAuthorization, string RemoteAddress) {
    public bool Enabled { get; init; } = true;
    public string LocalAddress { get; init; } = "Any";
    public string Protocol { get; init; } = "Any";
    public string LocalPort { get; init; } = "Any";
    public string RemotePort { get; init; } = "Any";
    public string Mode { get; init; } = "Transport";
    public string Profile { get; init; } = "Any";
    public string Phase1AuthSet { get; init; } = "IpsecStudio-P1";
    public string QuickModeCryptoSet { get; init; } = "IpsecStudio-QM";
}
public sealed record DriftGpoReading(string Name, bool Exists, string? Marker, int Version, bool Linked, DriftRuleReading[] Rules, string AuthorizationSddl, string Exemptions, string[] ApplyGroups, string[] DenySids) {
    public bool Enabled { get; init; } = true;
    public string[]? ApplySids { get; init; }
    public string[] Errors { get; init; } = [];
    public DriftCryptoReading? Crypto { get; init; }
}
// Proposals as "Encryption-Hash-KeyExchange" (main mode) and "Encapsulation-Encryption-ESPHash-MaxMinutes" (quick mode), in preference order.
public sealed record DriftCryptoReading(string[] MainMode, int MainModeMinutes, string[] QuickMode, string[] Phase1);
public sealed record DriftExternalReading(string Name, int RuleCount, string[] Links) {
    public string? Marker { get; init; }
    // Computers under the containers where the GPO is linked and enabled (block-inheritance, enforcement and security filtering not evaluated).
    public string[] Computers { get; init; } = [];
    public string[] InPlan { get; init; } = [];
}
// How the search went: GPOs in the domain, GPOs opened, and whether the firewall-extension filter could be trusted.
public sealed record DriftScan(int Total, int Opened, bool Filtered);
public sealed record DriftReading(DriftGpoReading[] Gpos, DriftExternalReading[] External) { public DriftScan? Scan { get; init; } }
public sealed record DriftGpoReport(string Name, bool Exists, string[] Differences);
public sealed record DriftReport(bool Clean, DriftGpoReport[] Gpos, DriftExternalReading[] External, DateTimeOffset Utc) { public DriftScan? Scan { get; init; } }

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
            if (!r.Enabled) diff.Add(L.T("drift.field", g.Name, "enabled", true, false));
            foreach (var rule in r.Rules.Where(x => !x.Enabled)) diff.Add(L.T("drift.field", rule.Name, "enabled", true, false));
            foreach (var duplicate in r.Rules.GroupBy(x => x.Name).Where(x => x.Count() > 1)) diff.Add(L.T("drift.ruleExtra", duplicate.Key));
            foreach (var rule in r.Rules) {
                void Field(string name,string expected,string found) { if(!expected.Equals(found,StringComparison.OrdinalIgnoreCase)) diff.Add(L.T("drift.field",rule.Name,name,expected,found)); }
                Field("localAddress","Any",rule.LocalAddress); Field("protocol","Any",rule.Protocol);
                Field("localPort","Any",rule.LocalPort); Field("remotePort","Any",rule.RemotePort);
                Field("mode","Transport",rule.Mode); Field("profile","Any",rule.Profile);
                if(rule.Name=="IpsecStudio-Main") { Field("phase1AuthSet","IpsecStudio-P1",rule.Phase1AuthSet); Field("quickModeCryptoSet","IpsecStudio-QM",rule.QuickModeCryptoSet); }
            }
            var main = r.Rules.FirstOrDefault(x => x.Name == "IpsecStudio-Main");
            if (main is null) diff.Add(L.T("drift.ruleMissing", "IpsecStudio-Main"));
            else {
                var scope = c.SecureSubnets.Length == 0 ? new[] { "Any" } : c.SecureSubnets;
                if (!SameAddresses(scope, main.RemoteAddress)) diff.Add(L.T("drift.field", main.Name, "remoteAddress", string.Join(",", scope), main.RemoteAddress));
                if (!main.Inbound.Equals(g.Inbound, StringComparison.OrdinalIgnoreCase)) diff.Add(L.T("drift.field", "IpsecStudio-Main", "inbound", g.Inbound, main.Inbound));
                if (!main.Outbound.Equals(g.Outbound, StringComparison.OrdinalIgnoreCase)) diff.Add(L.T("drift.field", "IpsecStudio-Main", "outbound", g.Outbound, main.Outbound));
                if (main.RequireAuthorization != g.RequireAuthorization) diff.Add(L.T("drift.field", "IpsecStudio-Main", "requireAuthorization", g.RequireAuthorization, main.RequireAuthorization));
            }
            var exempt = r.Rules.FirstOrDefault(x => x.Name == "IpsecStudio-Exempt");
            if (c.ExemptAddresses.Length > 0 && exempt is null) diff.Add(L.T("drift.ruleMissing", "IpsecStudio-Exempt"));
            else if (exempt is not null) {
                if (!exempt.Inbound.Equals("None", StringComparison.OrdinalIgnoreCase) || !exempt.Outbound.Equals("None", StringComparison.OrdinalIgnoreCase) || exempt.RequireAuthorization)
                    diff.Add(L.T("drift.field", exempt.Name, "security", "None/None/false", $"{exempt.Inbound}/{exempt.Outbound}/{exempt.RequireAuthorization}"));
                if (!SameAddresses(c.ExemptAddresses,exempt.RemoteAddress)) diff.Add(L.T("drift.field", "IpsecStudio-Exempt", "remoteAddress", string.Join(",", c.ExemptAddresses), exempt.RemoteAddress));
            }
            foreach (var extra in r.Rules.Where(x => x.Name is not ("IpsecStudio-Main" or "IpsecStudio-Exempt"))) diff.Add(L.T("drift.ruleExtra", extra.Name));
            // Windows reports an absent authorization list as "NotConfigured"; the compiler expresses it as null.
            var foundAuth = Normalize(r.AuthorizationSddl);
            if ((g.AuthorizationSddl ?? "") != foundAuth) diff.Add(L.T("drift.field", g.Name, "authorizationList", g.AuthorizationSddl ?? "-", foundAuth == "" ? "-" : foundAuth));
            if (r.ApplySids is not null) {
                if (!r.ApplySids.ToHashSet().SetEquals([g.ApplyGroupSid])) diff.Add(L.T("drift.field", g.Name, "applySid", g.ApplyGroupSid, string.Join(",",r.ApplySids)));
            } else {
                var applyExtra = r.ApplyGroups.Where(a => !a.Equals(g.ApplyGroup, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (applyExtra.Length > 0 || !r.ApplyGroups.Any(a => a.Equals(g.ApplyGroup, StringComparison.OrdinalIgnoreCase))) diff.Add(L.T("drift.field", g.Name, "apply", g.ApplyGroup, string.Join(",", r.ApplyGroups)));
            }
            if (!r.DenySids.ToHashSet().SetEquals(g.DenyGroupSids)) diff.Add(L.T("drift.field", g.Name, "denyApply", string.Join(",", g.DenyGroupSids), string.Join(",", r.DenySids)));
            var want = ExpectedCrypto(c, g);
            if (r.Crypto is null) diff.Add(L.T("drift.cryptoUnread"));
            else {
                void Seq(string set, string field, string[] expected, string[] found) { if (!expected.SequenceEqual(found ?? [], StringComparer.OrdinalIgnoreCase)) diff.Add(L.T("drift.field", set, field, string.Join(" > ", expected), string.Join(" > ", found ?? []))); }
                Seq("IpsecStudio-MM", "proposals", want.MainMode, r.Crypto.MainMode);
                if (r.Crypto.MainModeMinutes != want.MainModeMinutes) diff.Add(L.T("drift.field", "IpsecStudio-MM", "minutes", want.MainModeMinutes, r.Crypto.MainModeMinutes));
                Seq("IpsecStudio-QM", "proposals", want.QuickMode, r.Crypto.QuickMode);
                Seq("IpsecStudio-P1", "authentication", want.Phase1, r.Crypto.Phase1);
            }
            var icmp = Normalize(r.Exemptions) is "" or "None" ? "" : Normalize(r.Exemptions);
            if (!icmp.Equals(c.ExemptIcmp ? "Icmp" : "", StringComparison.OrdinalIgnoreCase)) diff.Add(L.T("drift.field", g.Name, "ICMP", c.ExemptIcmp ? "Icmp" : "-", icmp == "" ? "-" : icmp));
            gpos.Add(new(g.Name, true, diff.ToArray()));
        }
        var clean = gpos.All(x => x.Exists && x.Differences.Length == 0) && !reading.External.Any(Blocks);
        return new(clean, gpos.ToArray(), reading.External, DateTimeOffset.UtcNow) { Scan = reading.Scan };
    }
    // What the compiler writes into each GPO, in the format Windows returns (confirmed on the lab DC).
    public static DriftCryptoReading ExpectedCrypto(Compilation c, GpoSpec g) {
        var k = c.Crypto;
        var enc = $"ESP-{k.QuickModeEncryption}-{k.QuickModeIntegrity}-{k.QuickModeLifetimeMinutes}";
        var plain = $"ESP-None-{k.QuickModeIntegrity}-{k.QuickModeLifetimeMinutes}";
        return new([$"{k.MainModeEncryption}-{k.MainModeHash}-{k.KeyExchange}"], k.MainModeLifetimeMinutes, g.Encryption ? [enc] : [plain, enc], ["MachineKerb"]);
    }
    // NetSecurity returns IPv4 masks as dotted decimals even when the compiler supplied CIDR.
    public static bool SameAddresses(IEnumerable<string> expected, string found) => expected.Select(CanonicalAddress).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(found.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(CanonicalAddress));
    static string CanonicalAddress(string value) {
        var p=value.Trim().Split('/');
        if(p.Length>2 || !System.Net.IPAddress.TryParse(p[0],out var ip) || ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork) return value.Trim();
        var bits=32;
        if(p.Length==2 && !int.TryParse(p[1],out bits)) {
            if(!System.Net.IPAddress.TryParse(p[1],out var netmask) || netmask.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork) return value.Trim();
            var m=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(netmask.GetAddressBytes());var inverse=~m;
            if((inverse & unchecked(inverse+1))!=0) return value.Trim();
            bits=System.Numerics.BitOperations.PopCount(m);
        }
        if(bits is <0 or >32) return value.Trim();
        var mask=bits==0 ? 0u : uint.MaxValue << (32-bits);
        var network=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(ip.GetAddressBytes()) & mask;
        return $"{network>>24}.{(network>>16)&255}.{(network>>8)&255}.{network&255}/{bits}";
    }
    static string Normalize(string? value) => value is null || value.Equals("NotConfigured", StringComparison.OrdinalIgnoreCase) ? "" : value.Trim();
    // Only GPOs with an enabled link can affect computers; unlinked ones are listed without blocking (CV4.DS7).
    public static bool Blocks(DriftExternalReading e) => (e.Links ?? []).Length > 0;
    public static DriftReading Parse(string json) { var r = JsonSerializer.Deserialize<DriftReading>(json, Json.Settings) ?? new([], []); return r with { Gpos = r.Gpos ?? [], External = (r.External ?? []).Select(e => e with { Links = e.Links ?? [], Computers = e.Computers ?? [] }).ToArray() }; }
}
