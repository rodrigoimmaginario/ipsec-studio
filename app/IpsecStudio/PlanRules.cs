using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace IpsecStudio;

public static partial class PlanRules {
    public const string DomainComputersRid = "515";
    public static bool IsComputer(DirectoryObject o) => o.Kind is "server" or "workstation" or "dc" or "computer";
    [GeneratedRegex(@"\AS-1-5-21-\d{1,10}-\d{1,10}-\d{1,10}-\d{1,10}\z")] private static partial Regex DomainSidPattern();
    [GeneratedRegex(@"\AS-1-5-21-\d{1,10}-\d{1,10}-\d{1,10}\z")] private static partial Regex DomainRootSidPattern();
    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9 ._-]{0,63}\z")] private static partial Regex GroupNamePattern();
    public static bool ValidSid(string? sid) => sid is not null && DomainSidPattern().IsMatch(sid);
    public static bool ValidGroupName(string name) => GroupNamePattern().IsMatch(name);
    [GeneratedRegex(@"\A((CN|OU)=[^,=]+,)+(DC=[^,=]+,)*DC=[^,=]+\z", RegexOptions.IgnoreCase)] private static partial Regex ContainerDnPattern();
    [GeneratedRegex(@"\A[\p{L}\p{N} ._()-]{0,200}\z")] private static partial Regex DescriptionPattern();
    public static bool ValidContainerDn(string dn) => ContainerDnPattern().IsMatch(dn);
    // Membership after the first 'count' waves: groups gain the wave members. Used by simulation, probes and tests.
    public static InventorySnapshot AfterWaves(PlanInput plan, InventorySnapshot snapshot, int count) {
        var extra = new Dictionary<string, HashSet<string>>();
        foreach (var w in plan.Waves.Take(Math.Max(0, count))) foreach (var m in w.Members) {
            if (!extra.TryGetValue(m.GroupId, out var set)) extra[m.GroupId] = set = [];
            set.UnionWith(m.ComputerIds);
        }
        if (extra.Count == 0) return snapshot;
        return snapshot with { Objects = snapshot.Objects.Select(o => extra.TryGetValue(o.Id, out var add) ? o with { MemberIds = o.MemberIds.Union(add).ToArray() } : o).ToArray() };
    }
    public static bool ValidIpv4(string text) => IPAddress.TryParse(text, out var a) && a.AddressFamily == AddressFamily.InterNetwork && a.ToString() == text;
    public static bool ValidCidr(string text) {
        var parts = text.Split('/');
        return parts.Length == 2 && ValidIpv4(parts[0]) && int.TryParse(parts[1], out var bits) && bits is >= 8 and <= 32;
    }
    public static bool InCidr(string ip, string cidr) {
        var parts = cidr.Split('/'); var bits = int.Parse(parts[1]);
        uint ToUInt(string a) => BitConverter.ToUInt32(IPAddress.Parse(a).GetAddressBytes().Reverse().ToArray(), 0);
        var mask = bits == 0 ? 0u : uint.MaxValue << (32 - bits);
        return (ToUInt(ip) & mask) == (ToUInt(parts[0]) & mask);
    }
    public static HashSet<string> ComputersInGroup(string id, InventorySnapshot snapshot, List<string>? warnings = null) {
        var map = snapshot.Objects.ToDictionary(o => o.Id);
        var computers = new HashSet<string>(); var stack = new HashSet<string>(); var visited = new HashSet<string>();
        void Walk(string key) {
            if (stack.Contains(key)) { warnings?.Add(L.T("membership.cycle")); return; }
            if (!visited.Add(key)) return;
            if (!map.TryGetValue(key, out var o)) { warnings?.Add(L.T("membership.unresolved")); return; }
            if (IsComputer(o)) { computers.Add(o.Id); return; }
            if (o.Kind is not ("group" or "distributionGroup")) return;
            if (o.UnresolvedMemberCount > 0) warnings?.Add(L.T("membership.incomplete", o.Name));
            stack.Add(key);
            foreach (var member in o.MemberIds) Walk(member);
            stack.Remove(key);
        }
        Walk(id);
        return computers;
    }
    public static DirectoryObject? DomainComputers(InventorySnapshot s) => s.Objects.FirstOrDefault(o => o.Kind == "group" && o.Sid == s.DomainSid + "-" + DomainComputersRid);
    static void Require(bool condition, string message) { if (!condition) throw new ValidationException(message); }
    static void Text(string? value, int max, string label) => Require(value is not null && value.Trim().Length > 0 && value.Length <= max && !value.Any(char.IsControl), L.T("text.invalid", label, max));

    public static PlanAssessment Assess(PlanInput input, InventorySnapshot snapshot) {
        if (input is null) throw new ValidationException(L.T("plan.required"));
        Text(input.Name, 120, L.T("plan.name"));
        Require(input.InventoryId == snapshot.Id, L.T("plan.inventoryMismatch"));
        if (input.Settings is null || input.Restricted is null || input.Exemptions is null || input.ExternalHosts is null || input.Settings.Crypto is null || input.Settings.SecureSubnets is null || input.NewGroups is null || input.Waves is null) throw new ValidationException(L.T("plan.incomplete"));
        Require(input.NewGroups.Length <= 100 && input.Waves.Length <= 50, L.T("plan.limit"));
        Require(input.Restricted.Length <= 50 && input.Exemptions.Length <= 500 && input.ExternalHosts.Length <= 100 && input.Settings.SecureSubnets.Length <= 50, L.T("plan.limit"));
        Require(input.BaseMode is "secureRequest" or "request" or "require", L.T("plan.baseMode"));
        Require(DomainRootSidPattern().IsMatch(snapshot.DomainSid ?? ""), L.T("plan.domainSid"));
        var map = snapshot.Objects.ToDictionary(o => o.Id);
        DirectoryObject Group(string? id, string label) {
            Require(id is not null && map.TryGetValue(id, out var item) && item.Kind == "group", L.T("group.notFound", label));
            var g = map[id!];
            Require(ValidSid(g.Sid) && ValidGroupName(g.Name), L.T("group.invalid", label));
            return g;
        }
        var warnings = new List<string>(snapshot.Warnings);
        if (DateTimeOffset.UtcNow - snapshot.CapturedAt > TimeSpan.FromMinutes(30)) warnings.Add(L.T("inventory.old"));
        foreach (var cidr in input.Settings.SecureSubnets) Require(ValidCidr(cidr), L.T("subnet.invalid", cidr));
        Require(input.Settings.LinkTarget == "domain" || (map.Values.Any(o => o.Kind == "ou" && o.Dn.Equals(input.Settings.LinkTarget, StringComparison.OrdinalIgnoreCase))), L.T("link.invalid"));
        var c = input.Settings.Crypto;
        Require(c.MainModeEncryption is "AES128" or "AES192" or "AES256" && c.MainModeHash is "SHA256" or "SHA384" && c.KeyExchange is "DH14" or "DH19" or "DH20" or "DH24", L.T("crypto.mm"));
        Require(c.QuickModeEncryption is "AES128" or "AES192" or "AES256" or "AESGCM128" or "AESGCM256" && c.QuickModeIntegrity is "SHA256" or "AESGMAC256", L.T("crypto.qm"));
        Require(input.Settings.ReplicationWaitSeconds is >= 0 and <= 3600, L.T("replication.wait"));
        Require(c.MainModeLifetimeMinutes is >= 1 and <= 2879 && c.QuickModeLifetimeMinutes is >= 5 and <= 2879, L.T("crypto.lifetime"));

        var baseGroup = input.BaseGroupId is null ? DomainComputers(snapshot) : Group(input.BaseGroupId, L.T("label.base"));
        Require(baseGroup is not null, L.T("base.domainComputers"));
        var baseIds = ComputersInGroup(baseGroup!.Id, snapshot, warnings);
        var noPolicy = input.NoPolicyGroupId is null ? [] : ComputersInGroup(Group(input.NoPolicyGroupId, L.T("label.noPolicy")).Id, snapshot, warnings);
        var boundary = input.BoundaryGroupId is null ? [] : ComputersInGroup(Group(input.BoundaryGroupId, L.T("label.boundary")).Id, snapshot, warnings);
        var excluded = input.ExclusionGroupId is null ? [] : ComputersInGroup(Group(input.ExclusionGroupId, L.T("label.quarantine")).Id, snapshot, warnings);
        var management = input.Settings.ManagementGroupId is null ? [] : ComputersInGroup(Group(input.Settings.ManagementGroupId, L.T("label.management")).Id, snapshot, warnings);
        var restrictedIds = new HashSet<string>();
        var restrictedMap = new Dictionary<string, (RestrictedServers Rule, HashSet<string> Protected, HashSet<string> Access)>();
        foreach (var r in input.Restricted) {
            if (r is null) throw new ValidationException(L.T("restricted.null")); Require(Guid.TryParseExact(r.Id, "D", out _) && restrictedIds.Add(r.Id), L.T("restricted.id"));
            Text(r.Name, 100, L.T("restricted.name"));
            if (!r.Included) continue;
            var protectedGroup = Group(r.ProtectedGroupId, r.Name);
            var access = r.AccessGroupId is null ? new HashSet<string>() : ComputersInGroup(Group(r.AccessGroupId, L.T("label.access", r.Name)).Id, snapshot, warnings);
            var protectedIds = ComputersInGroup(protectedGroup.Id, snapshot, warnings);
            if (protectedIds.Count == 0) warnings.Add(L.T("restricted.noProtected", r.Name));
            if (r.AccessGroupId is not null && access.Count == 0) warnings.Add(L.T("restricted.noAccess", r.Name));
            if (r.AccessGroupId is null) warnings.Add(L.T("restricted.anyAccess", r.Name));
            restrictedMap[r.Id] = (r, protectedIds, access);
        }
        var exemptIds = new HashSet<string>(); var exemptAddresses = new List<string>();
        foreach (var e in input.Exemptions) {
            if (e is null) throw new ValidationException(L.T("exemption.null")); Require(Guid.TryParseExact(e.Id, "D", out _) && exemptIds.Add(e.Id), L.T("exemption.id"));
            Text(e.Name, 100, L.T("exemption.name")); Text(e.Reason, 500, L.T("exemption.reason")); Text(e.Owner, 100, L.T("exemption.owner"));
            Require(ValidIpv4(e.Address) || ValidCidr(e.Address), L.T("exemption.address", e.Name));
            if (e.Included) exemptAddresses.Add(e.Address);
        }
        var externalIds = new HashSet<string>();
        foreach (var x in input.ExternalHosts) {
            if (x is null) throw new ValidationException(L.T("external.null")); Require(Guid.TryParseExact(x.Id, "D", out _) && externalIds.Add(x.Id), L.T("external.id"));
            Text(x.Name, 64, L.T("external.name")); Require(ValidIpv4(x.Address), L.T("external.address", x.Name));
        }
        if (input.Settings.GroupContainer is not null) Require(ValidContainerDn(input.Settings.GroupContainer), L.T("groups.container"));
        var newGroupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in input.NewGroups) {
            if (g is null) throw new ValidationException(L.T("plan.incomplete"));
            Require(Guid.TryParseExact(g.Id, "D", out _) && ValidGroupName(g.Name) && DescriptionPattern().IsMatch(g.Description ?? ""), L.T("groups.invalid", g.Name));
            Require(newGroupNames.Add(g.Name) && !snapshot.Objects.Any(o => o.Kind == "group" && o.Name.Equals(g.Name, StringComparison.OrdinalIgnoreCase)), L.T("groups.exists", g.Name));
        }
        var profileGroupIds = new HashSet<string>(input.Restricted.Where(r => r.Included).SelectMany(r => new[] { r.ProtectedGroupId, r.AccessGroupId }).Where(x => x is not null).Select(x => x!));
        foreach (var id in new[] { input.BaseGroupId, input.NoPolicyGroupId, input.BoundaryGroupId, input.ExclusionGroupId, input.Settings.ManagementGroupId }) if (id is not null) profileGroupIds.Add(id);
        var waveIds = new HashSet<string>();
        foreach (var w in input.Waves) {
            if (w is null || w.Members is null) throw new ValidationException(L.T("plan.incomplete"));
            Require(Guid.TryParseExact(w.Id, "D", out _) && waveIds.Add(w.Id), L.T("waves.id"));
            Text(w.Name, 100, L.T("waves.name"));
            foreach (var m in w.Members) {
                Require(m is not null && profileGroupIds.Contains(m.GroupId), L.T("waves.group", w.Name));
                Require(m!.ComputerIds is { Length: > 0 and <= 10000 } && m.ComputerIds.All(c => map.TryGetValue(c, out var o) && IsComputer(o) && o.Kind != "dc" && ValidSid(o.Sid)), L.T("waves.computer", w.Name));
            }
            warnings.Add(L.T("waves.pending", w.Name, w.Members.Sum(m => m.ComputerIds.Length)));
        }
        var dcAddresses = snapshot.Objects.Where(o => o.Kind == "dc" && o.Ip is not null && ValidIpv4(o.Ip)).Select(o => o.Ip!).Order(StringComparer.Ordinal).ToArray();
        if (input.Settings.ExemptDomainControllers && dcAddresses.Length == 0) warnings.Add(L.T("dc.none"));
        if (!input.Settings.ExemptDomainControllers) warnings.Add(L.T("dc.notExempt"));

        var hosts = new List<HostProfile>();
        foreach (var o in snapshot.Objects.Where(IsComputer).OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)) {
            var memberships = new List<string>(); var conflicts = new List<string>();
            string profile = "none"; string? restrictedId = null;
            if (o.Kind == "dc") { memberships.Add("Domain Controllers"); profile = "dc"; }
            else {
                if (baseIds.Contains(o.Id)) memberships.Add(baseGroup.Name);
                var inRestricted = restrictedMap.Where(kv => kv.Value.Protected.Contains(o.Id)).ToArray();
                if (noPolicy.Contains(o.Id)) { profile = "noPolicy"; memberships.Add(map[input.NoPolicyGroupId!].Name); }
                if (inRestricted.Length > 1) conflicts.Add(L.T("conflict.multiRestricted", string.Join(", ", inRestricted.Select(kv => kv.Value.Rule.Name))));
                if (inRestricted.Length >= 1) { if (profile == "none") { profile = "restricted"; restrictedId = inRestricted[0].Key; } else conflicts.Add(L.T("conflict.noPolicyRestricted")); memberships.AddRange(inRestricted.Select(kv => map[kv.Value.Rule.ProtectedGroupId].Name)); }
                if (boundary.Contains(o.Id)) { if (profile == "none") profile = "boundary"; else conflicts.Add(L.T("conflict.boundary")); memberships.Add(map[input.BoundaryGroupId!].Name); }
                if (management.Contains(o.Id)) { if (profile == "none") profile = "management"; else conflicts.Add(L.T("conflict.management")); memberships.Add(map[input.Settings.ManagementGroupId!].Name); }
                if (profile == "none" && baseIds.Contains(o.Id)) profile = "base";
                if (profile != "none" && profile != "noPolicy" && !baseIds.Contains(o.Id)) warnings.Add(L.T("host.outsideBase", o.Name, profile));
                if (o.Ip is null) warnings.Add(L.T("host.noIp", o.Name));
                if (o.Ip is not null && exemptAddresses.Any(a => a == o.Ip || (ValidCidr(a) && InCidr(o.Ip, a)))) conflicts.Add(L.T("conflict.exemptAddress"));
            }
            var isExcluded = excluded.Contains(o.Id);
            if (isExcluded) { memberships.Add(map[input.ExclusionGroupId!].Name); if (o.Kind != "dc") { restrictedId = null; profile = "quarantine"; } }
            if (conflicts.Count > 0) warnings.Add(L.T("host.conflicts", o.Name, string.Join(" ", conflicts)));
            hosts.Add(new(o.Id, o.Name, o.Ip, profile, restrictedId, memberships.Distinct().ToArray(), isExcluded, conflicts.ToArray()));
        }
        var profiles = new List<ProfileSummary> {
            new("base", L.T("profile.base", baseGroup.Name), hosts.Where(h => h.Profile == "base").Select(h => h.ComputerId).ToArray(), [], [ModeText(input.BaseMode)]),
        };
        foreach (var kv in restrictedMap) profiles.Add(new("restricted:" + kv.Key, kv.Value.Rule.Name, hosts.Where(h => h.RestrictedId == kv.Key).Select(h => h.ComputerId).ToArray(), [],
            [kv.Value.Rule.Encryption ? L.T("profile.restricted.encrypted") : L.T("profile.restricted.auth"),
             kv.Value.Rule.AccessGroupId is null ? L.T("profile.access.any") : L.T("profile.access.group", map[kv.Value.Rule.AccessGroupId].Name, kv.Value.Access.Count)]));
        profiles.Add(new("exempt", L.T("profile.exempt"), hosts.Where(h => h.Profile == "noPolicy").Select(h => h.ComputerId).ToArray(), exemptAddresses.Concat(input.Settings.ExemptDomainControllers ? dcAddresses : []).Distinct().ToArray(), [L.T("profile.exempt.note")]));
        profiles.Add(new("boundary", L.T("profile.boundary"), hosts.Where(h => h.Profile == "boundary").Select(h => h.ComputerId).ToArray(), [], [L.T("profile.boundary.note")]));
        profiles.Add(new("exclusion", L.T("profile.exclusion"), hosts.Where(h => h.Excluded).Select(h => h.ComputerId).ToArray(), [], [L.T("profile.exclusion.note")]));
        if (management.Count > 0) profiles.Add(new("management", L.T("profile.management"), hosts.Where(h => h.Profile == "management").Select(h => h.ComputerId).ToArray(), [], [L.T("profile.management.note")]));
        if (input.BaseMode == "request") warnings.Add(L.T("base.requestWarning"));
        var strict = new List<string>();
        if (input.BaseMode == "require") strict.Add(L.T("label.base"));
        if (input.Settings.RequireOutbound) {
            strict.AddRange(input.Restricted.Where(r => r.Included).Select(r => r.Name));
            if (input.ExclusionGroupId is not null) strict.Add(L.T("label.quarantine"));
        }
        if (strict.Count > 0) warnings.Insert(0, L.T("outbound.requireAlert", string.Join(", ", strict)));
        return new(hosts.ToArray(), profiles.ToArray(), exemptAddresses.Distinct().ToArray(), dcAddresses, warnings.Distinct().ToArray());
    }
    // Windows modes (inbound, outbound) per profile; the compiler and the simulator share this single source.
    public static (string In, string Out) Modes(PlanInput input, string profile) => profile switch {
        "restricted" or "quarantine" => ("Require", input.Settings.RequireOutbound ? "Require" : "Request"),
        "boundary" => ("Request", "Request"),
        "base" or "management" => input.BaseMode switch { "require" => ("Require", "Require"), "request" => ("Request", "Request"), _ => ("Require", "Request") },
        _ => ("None", "None")
    };
    public static string ModeText(string mode) => mode switch {
        "secureRequest" => L.T("mode.secureRequest"),
        "request" => L.T("mode.request"),
        "require" => L.T("mode.require"),
        _ => mode
    };
}
