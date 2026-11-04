namespace IpsecStudio;

// One source for the allow-group meaning used by the simulator and the SDDL compiler.
public static class Authorization {
    public static string[] AllowedGroups(PlanInput input, string baseGroupId, string profile, RestrictedServers? restricted = null) {
        IEnumerable<string?> groups = profile switch {
            "restricted" => [restricted!.ProtectedGroupId, restricted.AccessGroupId ?? baseGroupId, input.Settings.ManagementGroupId],
            "quarantine" => [input.Settings.ManagementGroupId],
            "management" => [baseGroupId, input.ExclusionGroupId],
            "base" => [baseGroupId],
            _ => []
        };
        return groups.Where(g => g is not null).Select(g => g!).Distinct().ToArray();
    }
    public static bool DeniesExclusion(string profile) => profile is "base" or "restricted";
    // Own links are created enabled and not enforced. A lower OU can block them; missing OU evidence is not normal inheritance.
    public static Dictionary<string,bool?> Scopes(PlanInput input, InventorySnapshot snapshot) {
        var target=input.Settings.LinkTarget=="domain" ? snapshot.DomainDn : input.Settings.LinkTarget;
        var ous=snapshot.Objects.Where(o=>o.Kind=="ou" && o.Dn.Length>0).ToDictionary(o=>o.Dn,StringComparer.OrdinalIgnoreCase);
        var cache=new Dictionary<string,bool?>(StringComparer.OrdinalIgnoreCase);
        string Parent(string dn)=>System.Text.RegularExpressions.Regex.Replace(dn,@"^(?:[^,\\]|\\.)*,","");
        bool? Chain(string dn,int depth=0) {
            if(dn.Equals(target,StringComparison.OrdinalIgnoreCase)) return true;
            if(dn.Length==0 || depth>128) return null;
            if(cache.TryGetValue(dn,out var known)) return known;
            bool? blocked=false;
            if(dn.StartsWith("OU=",StringComparison.OrdinalIgnoreCase)) blocked=ous.TryGetValue(dn,out var ou) ? ou.BlocksInheritance : null;
            var parent=Parent(dn);
            var above=parent==dn ? null : Chain(parent,depth+1);
            var result=blocked==true || above==false ? false : blocked is null || above is null ? (bool?)null : true;
            cache[dn]=result;return result;
        }
        return snapshot.Objects.Where(PlanRules.IsComputer).ToDictionary(o=>o.Id,o=>o.Dn.Length==0 ? null : !InScope(input,o) ? false : Chain(Parent(o.Dn)));
    }
    public static bool InScope(PlanInput input, DirectoryObject host) => input.Settings.LinkTarget == "domain" || host.Dn.EndsWith("," + input.Settings.LinkTarget, StringComparison.OrdinalIgnoreCase);
}
