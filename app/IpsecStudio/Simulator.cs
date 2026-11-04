namespace IpsecStudio;

// Models the Windows IPsec decision for a new connection initiated by one host to another:
// most specific filter wins (exemptions), then negotiation modes of both sides, then mutual authorization.
// Outcomes: clear | authenticated | encrypted | blocked | indeterminate. It is a prediction, verified later by probes.
public static class Simulator {
    sealed record Endpoint(string Id, string Name, string? Ip, string Profile, bool Excluded, RestrictedServers? Restricted, HashSet<string> AllowedIds, bool ScopeKnown=true);
    public const int MaxMatrixHosts = 30;
    public static string[] MatrixIds(PlanInput plan, PlanAssessment a, string[]? selected = null) {
        var available = a.Hosts.Where(h => h.Profile != "dc").Select(h => h.ComputerId).Concat(plan.ExternalHosts.Select(x => x.Id)).ToHashSet();
        if (selected is not null && (selected.Length > MaxMatrixHosts || selected.Any(id => !available.Contains(id)))) throw new ValidationException(L.T("sim.selection"));
        return (selected ?? available.Take(MaxMatrixHosts).ToArray()).Distinct().ToArray();
    }
    public static Expectation[] Matrix(PlanInput plan, InventorySnapshot snapshot, PlanAssessment a, string[]? selected = null) {
        var ids = MatrixIds(plan, a, selected);
        var endpoints = ids.ToDictionary(id => id, id => Resolve(plan, snapshot, a, id));
        return ids.SelectMany(i => ids.Where(r => r != i).Select(r => Evaluate(plan, a, endpoints[i], endpoints[r]))).ToArray();
    }
    public static Expectation Evaluate(PlanInput plan, InventorySnapshot snapshot, PlanAssessment a, string initiator, string responder) => Evaluate(plan, a, Resolve(plan, snapshot, a, initiator), Resolve(plan, snapshot, a, responder));
    static Expectation Evaluate(PlanInput plan, PlanAssessment a, Endpoint i, Endpoint r) {
        var reasons = new List<string>();
        Expectation Result(string outcome) => new(i.Name, r.Name, outcome, reasons.ToArray());
        if (i.Id == r.Id) { reasons.Add(L.T("sim.sameHost")); return Result("indeterminate"); }
        if (i.Profile == "dc" || r.Profile == "dc") { reasons.Add(L.T("sim.dc")); return Result(plan.Settings.ExemptDomainControllers ? "clear" : "indeterminate"); }
        if(!i.ScopeKnown || !r.ScopeKnown){reasons.Add(L.T("sim.scopeUnknown"));return Result("indeterminate");}
        bool Exempt(Endpoint e) => e.Ip is not null && a.ExemptAddresses.Any(x => x == e.Ip || (PlanRules.ValidCidr(x) && PlanRules.InCidr(e.Ip, x)));
        if (Exempt(i) || Exempt(r)) { reasons.Add(L.T("sim.exempt")); return Result("clear"); }
        if (i.Ip is null || r.Ip is null) { reasons.Add(L.T("sim.noIp")); return Result("indeterminate"); }
        else if (i.Ip == r.Ip) { reasons.Add(L.T("sim.sharedIp")); return Result("indeterminate"); }
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
            // Management opens/preserves SAs for WinRM. They are bidirectional, so a unilateral ACL is not proof of denial in reverse.
            if((i.Profile=="management" || r.Profile=="management") && Authorizes(plan,i,r)) {
                reasons.Add(L.T("sim.managementSaUnknown"));return Result("indeterminate");
            }
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
        if (remote.Excluded && Authorization.DeniesExclusion(local.Profile)) return false;
        return local.AllowedIds.Contains(remote.Id);
    }
    static (string Out, string In) Modes(PlanInput plan, Endpoint e) {
        var (i, o) = PlanRules.Modes(plan, e.Profile);
        return (o.ToLowerInvariant(), i.ToLowerInvariant());
    }
    static Endpoint Resolve(PlanInput plan, InventorySnapshot snapshot, PlanAssessment a, string key) {
        var host = a.Hosts.FirstOrDefault(h => h.ComputerId == key);
        if (host is not null) {
            RestrictedServers? rule = host.RestrictedId is null ? null : plan.Restricted.First(x => x.Id == host.RestrictedId);
            var baseId = plan.BaseGroupId ?? PlanRules.DomainComputers(snapshot)!.Id;
            var allowed = Authorization.AllowedGroups(plan, baseId, host.Profile, rule).SelectMany(g => PlanRules.ComputersInGroup(g, snapshot)).ToHashSet();
            return new(host.ComputerId, host.Name, host.Ip, host.Profile, host.Excluded, rule, allowed, host.ScopeKnown);
        }
        var ext = plan.ExternalHosts.FirstOrDefault(x => x.Id == key);
        if (ext is not null) return new(ext.Id, ext.Name, ext.Address, "external", false, null, []);
        if (PlanRules.ValidIpv4(key)) return new(key, key, key, "external", false, null, []);
        throw new ValidationException(L.T("sim.hostNotFound"));
    }
}
