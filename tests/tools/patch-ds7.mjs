// One-off patch for CV4.DS7 (replication wait setting, pending DCs, external GPO reach/scan). Safe to delete after use.
import fs from 'node:fs';
const read = (f) => fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n');
const edit = (f, pairs) => { let s = read(f); for (const [a, b] of pairs) { if (!s.includes(a)) throw new Error(f + ' missing: ' + a.slice(0, 70)); s = s.replace(a, () => b); } fs.writeFileSync(f, s); };
edit('app/IpsecStudio/Models.cs', [[`    public bool RequireOutbound { get; init; }`, `    public bool RequireOutbound { get; init; }
    // After apply/rollback/waves the Studio waits this long for every reachable DC to show the change (0-3600 s).
    public int ReplicationWaitSeconds { get; init; } = 180;`]]);
edit('app/IpsecStudio/PlanRules.cs', [[`        Require(c.MainModeLifetimeMinutes is >= 1 and <= 2879`, `        Require(input.Settings.ReplicationWaitSeconds is >= 0 and <= 3600, L.T("replication.wait"));
        Require(c.MainModeLifetimeMinutes is >= 1 and <= 2879`]]);
edit('app/IpsecStudio/L10n.cs', [[`        ["deploy.outsideInvalid"]`, `        ["replication.wait"] = ("Espera de replicação deve ficar entre 0 e 3600 segundos.", "Replication wait must be between 0 and 3600 seconds."),
        ["deploy.outsideInvalid"]`]]);
edit('app/IpsecStudio/Compiler.cs', [
  [`            crypto = input.Settings.Crypto,`, `            crypto = input.Settings.Crypto, replicationWaitSeconds = input.Settings.ReplicationWaitSeconds,`],
  [`  $deadline=(Get-Date).AddSeconds(180); $state=[ordered]@{}`, `  $deadline=(Get-Date).AddSeconds([int]$spec.replicationWaitSeconds); $state=[ordered]@{}`],
  [`$deadline=(Get-Date).AddSeconds(180); $repl=[ordered]@{}`, `$deadline=(Get-Date).AddSeconds(__REPLWAIT__); $repl=[ordered]@{}`],
  [`+ NL + WaitMembership + NL + "@{marker=$marker;changed=$changed;replication=$repl}|ConvertTo-Json -Compress";
        var rollback`, `+ NL + WaitMembership.Replace("__REPLWAIT__", plan.Input.Settings.ReplicationWaitSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)) + NL + "@{marker=$marker;changed=$changed;replication=$repl}|ConvertTo-Json -Compress";
        var rollback`],
  [`+ NL + WaitMembership + NL + "@{marker=$marker;changed=$changed;replication=$repl}|ConvertTo-Json -Compress";
        var hash`, `+ NL + WaitMembership.Replace("__REPLWAIT__", plan.Input.Settings.ReplicationWaitSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)) + NL + "@{marker=$marker;changed=$changed;replication=$repl}|ConvertTo-Json -Compress";
        var hash`]]);
edit('app/IpsecStudio/Drift.cs', [
  [`public sealed record DriftExternalReading(string Name, int RuleCount, string[] Links);`, `public sealed record DriftExternalReading(string Name, int RuleCount, string[] Links) {
    // Computers under the containers where the GPO is linked and enabled (block-inheritance, enforcement and security filtering not evaluated).
    public string[] Computers { get; init; } = [];
    public string[] InPlan { get; init; } = [];
}
// How the search went: GPOs in the domain, GPOs opened, and whether the firewall-extension filter could be trusted.
public sealed record DriftScan(int Total, int Opened, bool Filtered);`],
  [`public sealed record DriftReading(DriftGpoReading[] Gpos, DriftExternalReading[] External);`, `public sealed record DriftReading(DriftGpoReading[] Gpos, DriftExternalReading[] External) { public DriftScan? Scan { get; init; } }`],
  [`public sealed record DriftReport(bool Clean, DriftGpoReport[] Gpos, DriftExternalReading[] External, DateTimeOffset Utc);`, `public sealed record DriftReport(bool Clean, DriftGpoReport[] Gpos, DriftExternalReading[] External, DateTimeOffset Utc) { public DriftScan? Scan { get; init; } }`],
  [`        return new(clean, gpos.ToArray(), reading.External, DateTimeOffset.UtcNow);`, `        return new(clean, gpos.ToArray(), reading.External, DateTimeOffset.UtcNow) { Scan = reading.Scan };`],
  [`return r with { Gpos = r.Gpos ?? [], External = r.External ?? [] }; }`, `return r with { Gpos = r.Gpos ?? [], External = (r.External ?? []).Select(e => e with { Links = e.Links ?? [], Computers = e.Computers ?? [] }).ToArray() }; }`]]);
let d = read('app/IpsecStudio/Deployment.cs');
const repD = (a, b) => { if (!d.includes(a)) throw new Error('Deployment missing: ' + a.slice(0, 70)); d = d.replace(a, () => b); };
repD(`    public bool? ExpansionAllowed { get; init; }
}`, `    public bool? ExpansionAllowed { get; init; }
    // DCs that did not confirm the change within the replication wait ("timeout"/"unreachable"): hosts using them may keep old policy.
    public string[] PendingDcs { get; init; } = [];
}`);
repD(`public sealed record WaveResult(string WaveId, string WaveName, bool Succeeded, bool ExpansionAllowed, string Output, HostResult[] Verify, ProbeResult[] Probe);`,
`public sealed record WaveResult(string WaveId, string WaveName, bool Succeeded, bool ExpansionAllowed, string Output, HostResult[] Verify, ProbeResult[] Probe) {
    public string[] PendingDcs { get; init; } = [];
}`);
repD(`            var record = new DeploymentRecord(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, hash, request.Action, ok, output) { Approval = request.Approval?.Trim() };`,
`            var record = new DeploymentRecord(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, hash, request.Action, ok, output) { Approval = request.Approval?.Trim(), PendingDcs = ok ? PendingDcs(output) : [] };`);
repD(`            return new(wave.WaveId, wave.Name, ok, apply && expansion, output, verify, probe);`, `            return new(wave.WaveId, wave.Name, ok, apply && expansion, output, verify, probe) { PendingDcs = ok ? PendingDcs(output) : [] };`);
repD(`    public static string RefreshScriptText => RefreshScript;`, `    public static string RefreshScriptText => RefreshScript;
    public static string DriftScriptText => DriftScript;
    // Reads the "replication" map the generated scripts print ({dc: ok|timeout|unreachable}) and returns the DCs not "ok".
    public static string[] PendingDcs(string output) {
        try {
            using var doc = JsonDocument.Parse(output);
            if (!doc.RootElement.TryGetProperty("replication", out var r) || r.ValueKind != JsonValueKind.Object) return [];
            return r.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String && p.Value.GetString() != "ok").Select(p => p.Name).ToArray();
        } catch (JsonException) { return []; }
    }`);
repD(`        var reading = IpsecStudio.Drift.Parse(output);
        var report = IpsecStudio.Drift.Compare(c, reading);`, `        var reading = IpsecStudio.Drift.Parse(output);
        var planHosts = PlanRules.Assess(plan.Input, plan.Inventory).Hosts.Where(h => h.Profile != "dc").Select(h => h.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        reading = reading with { External = reading.External.Select(e => e with { InPlan = e.Computers.Where(planHosts.Contains).ToArray() }).ToArray() };
        var report = IpsecStudio.Drift.Compare(c, reading);`);
repD(String.raw`$external=@()
foreach($g in (Get-GPO -All | Where-Object { $_.DisplayName -notin @($spec.gpos) })){
  $n=@(Get-NetIPsecRule -PolicyStore ($spec.domain+'\'+$g.DisplayName) -ErrorAction Stop).Count
  if($n -gt 0){ $report=[xml](Get-GPOReport -Guid $g.Id -ReportType Xml); $external+=@{name=$g.DisplayName;ruleCount=$n;links=@($report.GPO.LinksTo | ForEach-Object { $_.SOMPath })} }
}
@{gpos=$gpos;external=$external} | ConvertTo-Json -Compress -Depth 6`, String.raw`# Other GPOs with connection security rules. Rules written by the NetSecurity cmdlets or the firewall snap-in register the
# Windows Firewall extension in gPCMachineExtensionNames (lab, CV4.DS7), so one LDAP query selects the candidates; only those are opened.
# If the Studio's own GPOs exist without that extension the attribute is not trusted here and every GPO is opened.
$fwExt='{B05566AC-FE9C-4368-BE01-7A4CBB6CBA11}'
$base=$domain.DistinguishedName
$all=@(Get-ADObject -LDAPFilter '(objectClass=groupPolicyContainer)' -SearchBase "CN=Policies,CN=System,$base" -Properties displayName,gPCMachineExtensionNames)
$ours=@($all | Where-Object { $_.displayName -in @($spec.gpos) })
$filtered=($ours.Count -eq 0) -or (@($ours | Where-Object { "$($_.gPCMachineExtensionNames)" -like "*$fwExt*" }).Count -gt 0)
$candidates=@($all | Where-Object { $_.displayName -notin @($spec.gpos) -and (-not $filtered -or "$($_.gPCMachineExtensionNames)" -like "*$fwExt*") })
$external=@()
foreach($c in $candidates){
  $n=@(Get-NetIPsecRule -PolicyStore ($spec.domain+'\'+$c.displayName) -ErrorAction Stop).Count
  if($n -gt 0){
    $guid=$c.Name
    $pattern='\[LDAP://cn='+[regex]::Escape($guid)+',[^;\]]*;(\d)\]'
    $links=@(Get-ADObject -LDAPFilter "(gPLink=*$guid*)" -SearchBase $base -Properties gPLink | Where-Object { $m=[regex]::Match([string]$_.gPLink,$pattern,'IgnoreCase'); $m.Success -and (([int]$m.Groups[1].Value) -band 1) -eq 0 } | ForEach-Object { $_.DistinguishedName })
    $computers=@($links | ForEach-Object { Get-ADComputer -SearchBase $_ -SearchScope Subtree -Filter * | ForEach-Object { $_.Name } } | Sort-Object -Unique)
    $external+=@{name=$c.displayName;ruleCount=$n;links=$links;computers=@($computers | Select-Object -First 500)}
  }
}
@{gpos=$gpos;external=$external;scan=@{total=$all.Count;opened=$candidates.Count;filtered=$filtered}} | ConvertTo-Json -Compress -Depth 6`);
fs.writeFileSync('app/IpsecStudio/Deployment.cs', d);
console.log('patched');
