namespace IpsecStudio;

// Models the Windows IPsec decision for a new connection initiated by one host to another:
// most specific filter wins (exemptions), then negotiation modes of both sides, then mutual authorization.
// Outcomes: clear | authenticated | encrypted | blocked | indeterminate. It is a prediction, verified later by probes.
public static class Simulator {
    sealed record Endpoint(string Id, string Name, string? Ip, string Profile, bool Excluded, bool Managed, RestrictedServers? Restricted, HashSet<string> AccessIds, HashSet<string> ProtectedIds);
    public static Expectation[] Matrix(PlanInput plan, InventorySnapshot snapshot, PlanAssessment a) {
        var ids = a.Hosts.Where(h => h.Profile != "dc").Select(h => h.ComputerId).Concat(plan.ExternalHosts.Select(x => x.Id)).ToArray();
        return ids.SelectMany(i => ids.Where(r => r != i).Select(r => Evaluate(plan, snapshot, a, i, r))).ToArray();
    }
    public static Expectation Evaluate(PlanInput plan, InventorySnapshot snapshot, PlanAssessment a, string initiator, string responder) {
        var i = Resolve(plan, snapshot, a, initiator); var r = Resolve(plan, snapshot, a, responder);
        var reasons = new List<string>();
        Expectation Result(string outcome) => new(i.Name, r.Name, outcome, reasons.ToArray());
        if (i.Id == r.Id) { reasons.Add(L.T("sim.sameHost")); return Result("indeterminate"); }
        if (i.Profile == "dc" || r.Profile == "dc") { reasons.Add(L.T("sim.dc")); return Result(plan.Settings.ExemptDomainControllers ? "clear" : "indeterminate"); }
        bool Exempt(Endpoint e) => e.Ip is not null && a.ExemptAddresses.Any(x => x == e.Ip || (PlanRules.ValidCidr(x) && PlanRules.InCidr(e.Ip, x)));
        if (Exempt(i) || Exempt(r)) { reasons.Add(L.T("sim.exempt")); return Result("clear"); }
        if (i.Ip is null || r.Ip is null) { reasons.Add(L.T("sim.noIp")); }
        else if (plan.Settings.SecureSubnets.Length > 0 && !(plan.Settings.SecureSubnets.Any(s => PlanRules.InCidr(i.Ip, s)) && plan.Settings.SecureSubnets.Any(s => PlanRules.InCidr(r.Ip, s)))) {
            reasons.Add(L.T("sim.outsideSubnet")); return Result("clear");
        }
        var (iOut, _) = Modes(plan, i); var (_, rIn) = Modes(plan, r);
        reasons.Add(L.T("sim.modes", i.Name, iOut, r.Name, rIn));
        if (iOut == "none" && rIn == "none") { reasons.Add(L.T("sim.noPolicy")); return Result("clear"); }
        if (iOut == "none") { if (rIn == "require") { reasons.Add(L.T("sim.initNone.blocked")); return Result("blocked"); } reasons.Add(L.T("sim.initNone.clear")); return Result("clear"); }
        if (rIn == "none") { if (iOut == "require") { reasons.Add(L.T("sim.respNone.blocked")); return Result("blocked"); } reasons.Add(L.T("sim.respNone.clear")); return Result("clear"); }
        // Both sides negotiate. The responder's authorization list decides whether the initiator may connect
        // (observed in the lab on 27/10: enforced on the receiving side only, like "Access this computer from the network").
        if (!Authorizes(plan, r, i)) {
            reasons.Add(L.T("sim.unauthorized", r.Name, i.Name));
            return Result("blocked");
        }
        var encrypted = (i.Restricted?.Encryption ?? false) || (r.Restricted?.Encryption ?? false);
        reasons.Add(encrypted ? L.T("sim.encrypted") : L.T("sim.authenticated"));
        return Result(encrypted ? "encrypted" : "authenticated");
    }
    static bool RequiresAuthorization(Endpoint e) => e.Profile is "base" or "management" or "restricted" or "quarantine";
    static bool Authorizes(PlanInput plan, Endpoint local, Endpoint remote) {
        if (!RequiresAuthorization(local)) return true;
        if (remote.Profile == "external") return false;
        if (local.Profile == "quarantine") return remote.Profile == "management";
        if (remote.Profile == "quarantine" && local.Profile != "management") return false;
        if (local.Profile == "restricted" && local.Restricted!.AccessGroupId is not null)
            return local.AccessIds.Contains(remote.Id) || local.ProtectedIds.Contains(remote.Id);
        return remote.Profile is not ("noPolicy" or "external");
    }
    static (string Out, string In) Modes(PlanInput plan, Endpoint e) {
        var (i, o) = PlanRules.Modes(plan, e.Profile);
        return (o.ToLowerInvariant(), i.ToLowerInvariant());
    }
    static Endpoint Resolve(PlanInput plan, InventorySnapshot snapshot, PlanAssessment a, string key) {
        var host = a.Hosts.FirstOrDefault(h => h.ComputerId == key);
        if (host is not null) {
            RestrictedServers? rule = host.RestrictedId is null ? null : plan.Restricted.First(x => x.Id == host.RestrictedId);
            var access = rule?.AccessGroupId is null ? [] : PlanRules.ComputersInGroup(rule.AccessGroupId, snapshot);
            var prot = rule is null ? [] : PlanRules.ComputersInGroup(rule.ProtectedGroupId, snapshot);
            return new(host.ComputerId, host.Name, host.Ip, host.Profile, host.Excluded, host.Profile == "management", rule, access, prot);
        }
        var ext = plan.ExternalHosts.FirstOrDefault(x => x.Id == key);
        if (ext is not null) return new(ext.Id, ext.Name, ext.Address, "external", false, false, null, [], []);
        if (PlanRules.ValidIpv4(key)) return new(key, key, key, "external", false, false, null, [], []);
        throw new ValidationException(L.T("sim.hostNotFound"));
    }
}
