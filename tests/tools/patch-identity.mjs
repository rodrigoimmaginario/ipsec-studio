// One-off patch for CV4.DS2: identity propagation state in verification. Safe to delete after use.
import fs from 'node:fs';
const read = (f) => fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n');
const rep = (s, a, b) => { if (!s.includes(a)) throw new Error('missing: ' + a.slice(0, 70)); return s.replace(a, b); };

let s = read('app/IpsecStudio/Deployment.cs');
s = rep(s, `public sealed record HostResult(string Host, bool Reachable, string Output);`,
`public sealed record HostResult(string Host, bool Reachable, string Output) {
    // Identity propagation: the machine TGT (logon session 0x3e7) carries the groups IPsec peers will see.
    public string IdentityState { get; init; } = "unknown";
    public DateTimeOffset? TgtStartUtc { get; init; }
    public DateTimeOffset? LastGroupChangeUtc { get; init; }
}`);
s = rep(s, `    async Task<HostResult[]> RunHosts(SavedPlan plan, string[]? computerIds, string action, string identity) {
        var hosts = TargetHosts(plan, computerIds);
        var script = (action == "refresh" ? RefreshScript : VerifyScript).Replace("__HOSTS__", JsonSerializer.Serialize(hosts.Select(h => h.Name + "." + plan.Inventory.Domain)));
        var (ok, output) = await runner.Run(script, TimeSpan.FromMinutes(6));
        await repository.AppendDeployment(new(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, "", action, ok, output));
        if (!ok) throw new InvalidOperationException(L.T("deploy.remoteFailed", output));
        return JsonSerializer.Deserialize<HostResult[]>(output, Json.Settings) ?? [];
    }`,
`    async Task<HostResult[]> RunHosts(SavedPlan plan, string[]? computerIds, string action, string identity) {
        var hosts = TargetHosts(plan, computerIds);
        var script = (action == "refresh" ? RefreshScript : VerifyScript).Replace("__HOSTS__", JsonSerializer.Serialize(hosts.Select(h => h.Name + "." + plan.Inventory.Domain)))
            .Replace("__GROUPSIDS__", JsonSerializer.Serialize(ProfileGroupSids(plan)));
        var (ok, output) = await runner.Run(script, TimeSpan.FromMinutes(6));
        await repository.AppendDeployment(new(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, "", action, ok, output));
        if (!ok) throw new InvalidOperationException(L.T("deploy.remoteFailed", output));
        var results = JsonSerializer.Deserialize<HostResult[]>(output, Json.Settings) ?? [];
        return action == "verify" ? results.Select(WithIdentity).ToArray() : results;
    }
    static string[] ProfileGroupSids(SavedPlan plan) {
        var map = plan.Inventory.Objects.ToDictionary(o => o.Id);
        var ids = new List<string?> { plan.Input.BaseGroupId, plan.Input.NoPolicyGroupId, plan.Input.BoundaryGroupId, plan.Input.ExclusionGroupId, plan.Input.Settings.ManagementGroupId };
        ids.AddRange(plan.Input.Restricted.Where(r => r.Included).SelectMany(r => new[] { r.ProtectedGroupId, r.AccessGroupId }));
        var dc = PlanRules.DomainComputers(plan.Inventory); if (plan.Input.BaseGroupId is null && dc is not null) ids.Add(dc.Id);
        return ids.Where(id => id is not null && map.TryGetValue(id, out var g) && PlanRules.ValidSid(g.Sid)).Select(id => map[id!].Sid!).Distinct().ToArray();
    }
    static HostResult WithIdentity(HostResult h) {
        if (!h.Reachable) return h;
        try {
            using var doc = JsonDocument.Parse(h.Output);
            DateTimeOffset? Read(string name) => doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(v.GetString(), null, System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d.ToUniversalTime() : null;
            var tgt = Read("tgtStartUtc"); var change = Read("lastGroupChangeUtc");
            return h with { IdentityState = IdentityState(tgt, change), TgtStartUtc = tgt, LastGroupChangeUtc = change };
        } catch (JsonException) { return h; }
    }
    public static string IdentityState(DateTimeOffset? tgtStart, DateTimeOffset? lastGroupChange) =>
        tgtStart is null || lastGroupChange is null ? "unknown" : tgtStart >= lastGroupChange ? "synced" : "pending";`);
// verify script: read group whenChanged on the Studio host, TGT start on each host
s = rep(s, `    const string VerifyScript = RemotePreamble + NL + """
    $out=Invoke-Command -ComputerName $h -ScriptBlock {`,
`    const string VerifyScript = RemotePreamble + NL + """
    $out=Invoke-Command -ComputerName $h -ArgumentList $lastChange -ScriptBlock {
      param($lastChangeUtc)
      $tgt=$null
      try{ $text=(klist.exe -li 0x3e7 tgt | Out-String); if($text -match 'Start Time:\\s*([^\\r\\n]+?)\\s*\\(local\\)'){ $tgt=[datetime]::Parse($matches[1],[Globalization.CultureInfo]::CurrentCulture).ToUniversalTime().ToString('o') } }catch{}`);
s = rep(s, `      @{computer=$env:COMPUTERNAME;studioRules=$rules;enabledIpsecRules=$all;authorizationList=[string]$setting.RemoteMachineTransportAuthorizationList;exemptions=[string]$setting.Exemptions;gpos=@($rules | ForEach-Object {$_.source} | Sort-Object -Unique);healthy=(@($rules | Where-Object {$_.status -ne 'OK'}).Count -eq 0 -and $rules.Count -gt 0)}`,
`      @{computer=$env:COMPUTERNAME;studioRules=$rules;enabledIpsecRules=$all;authorizationList=[string]$setting.RemoteMachineTransportAuthorizationList;exemptions=[string]$setting.Exemptions;gpos=@($rules | ForEach-Object {$_.source} | Sort-Object -Unique);healthy=(@($rules | Where-Object {$_.status -ne 'OK'}).Count -eq 0 -and $rules.Count -gt 0);tgtStartUtc=$tgt;lastGroupChangeUtc=$lastChangeUtc}`);
s = rep(s, `$hosts=ConvertFrom-Json @'
__HOSTS__
'@
$results=@()`,
`$hosts=ConvertFrom-Json @'
__HOSTS__
'@
$groupSids=ConvertFrom-Json @'
__GROUPSIDS__
'@
# Last change of any profile group (membership edits update whenChanged): hosts whose machine TGT predates it may still carry old groups.
$lastChange=$null
try{ Import-Module ActiveDirectory -ErrorAction Stop; $times=@(foreach($sid in @($groupSids)){ (Get-ADGroup -Identity $sid -Properties whenChanged).whenChanged }); if($times.Count){ $lastChange=($times | Sort-Object | Select-Object -Last 1).ToUniversalTime().ToString('o') } }catch{}
$results=@()`);
fs.writeFileSync('app/IpsecStudio/Deployment.cs', s);

// RefreshScript does not use __GROUPSIDS__ but the replace is harmless. Program/UI: nothing server-side else.
console.log('patched');
