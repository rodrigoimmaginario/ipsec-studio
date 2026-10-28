// One-off patch for CV4.DS3: drift reading script, gate in Execute, API, L10n. Safe to delete after use.
import fs from 'node:fs';
const read = (f) => fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n');
const rep = (s, a, b) => { if (!s.includes(a)) throw new Error('missing: ' + a.slice(0, 70)); return s.replace(a, b); };

let s = read('app/IpsecStudio/Deployment.cs');
s = rep(s, `public sealed record DeployRequest(string PlanId, int Version, string Hash, string Action, string? Approval = null);`,
`public sealed record DeployRequest(string PlanId, int Version, string Hash, string Action, string? Approval = null) {
    // Drift (edited Studio GPOs or external IPsec GPOs) blocks apply/rollback unless the operator acknowledges it explicitly.
    public bool AcknowledgeDrift { get; init; }
}`);
s = rep(s, `            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(request.Hash ?? "")))
                throw new ConflictException(L.T("deploy.hash"));
            var (ok, output) = await runner.Run(script, TimeSpan.FromMinutes(10));`,
`            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(request.Hash ?? "")))
                throw new ConflictException(L.T("deploy.hash"));
            if (request.Action is "apply" or "rollback") {
                var drift = await ReadDrift(plan, request.Action == "apply");
                if (!drift.Clean) {
                    if (!request.AcknowledgeDrift) throw new ConflictException(L.T("drift.blocked", Describe(drift)));
                    await repository.AppendDeployment(new(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, hash, "drift-acknowledged", true, JsonSerializer.Serialize(drift, Json.Settings)) { Approval = request.Approval?.Trim() });
                }
            }
            var (ok, output) = await runner.Run(script, TimeSpan.FromMinutes(10));`);
s = rep(s, `    // A wave adds (or removes) members of profile groups, then refreshes, verifies and probes only the wave's hosts.`,
`    public async Task<DriftReport> Drift(string planId, int version) {
        if (!Enabled) throw new UnauthorizedAccessException(L.T("deploy.disabled"));
        var plan = await repository.Get(planId) ?? throw new ValidationException(L.T("deploy.planNotFound"));
        if (plan.Version != version) throw new ConflictException(L.T("deploy.stale"));
        return await ReadDrift(plan, false);
    }
    // Before apply the Studio GPOs are expected to be absent: only external IPsec GPOs count. Before rollback/verify, every GPO must match.
    async Task<DriftReport> ReadDrift(SavedPlan plan, bool beforeApply) {
        var c = Compiler.Compile(plan);
        var spec = new { domain = plan.Inventory.Domain, target = plan.Input.Settings.LinkTarget == "domain" ? plan.Inventory.DomainDn : plan.Input.Settings.LinkTarget, gpos = c.Gpos.Select(g => g.Name) };
        var (ok, output) = await runner.Run(DriftScript.Replace("__DRIFT__", JsonSerializer.Serialize(spec)), TimeSpan.FromMinutes(5));
        if (!ok) throw new InvalidOperationException(L.T("deploy.remoteFailed", output));
        var reading = IpsecStudio.Drift.Parse(output);
        var report = IpsecStudio.Drift.Compare(c, reading);
        if (beforeApply) { var missing = report.Gpos.Where(g => !g.Exists).Select(g => g with { Differences = [] }).ToArray(); var present = report.Gpos.Where(g => g.Exists).ToArray(); report = report with { Gpos = [.. missing, .. present], Clean = present.Length == 0 && reading.External.Length == 0 }; }
        return report;
    }
    static string Describe(DriftReport r) => string.Join(" | ", r.Gpos.Where(g => g.Differences.Length > 0).Select(g => g.Name + ": " + string.Join("; ", g.Differences)).Concat(r.External.Select(e => L.T("drift.external", e.Name, e.RuleCount))));
    // A wave adds (or removes) members of profile groups, then refreshes, verifies and probes only the wave's hosts.`);
s = rep(s, `    const string RemotePreamble = """`, `    // Reads Studio GPOs and any other GPO with connection security rules. Group names come from GPO permissions (display only).
    const string DriftScript = """
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$spec=ConvertFrom-Json @'
__DRIFT__
'@
Import-Module GroupPolicy; Import-Module ActiveDirectory
$domain=Get-ADDomain
if($domain.DNSRoot -ne $spec.domain){throw "Wrong domain: $($domain.DNSRoot)"}
$apply=[guid]'edacfd8f-ffb3-11d1-b41d-00a0c968f939'
$links=@{}
foreach($l in (Get-GPInheritance -Target $spec.target).GpoLinks){ $links[$l.DisplayName]=[bool]$l.Enabled }
$gpos=@()
foreach($name in @($spec.gpos)){
  $g=Get-GPO -Name $name -ErrorAction SilentlyContinue
  if(-not $g){ $gpos+=@{name=$name;exists=$false;marker=$null;version=0;linked=$false;rules=@();authorizationSddl='';exemptions='';applyGroups=@();denySids=@()}; continue }
  $store=$spec.domain+'\'+$name
  $rules=@(Get-NetIPsecRule -PolicyStore $store -ErrorAction SilentlyContinue | ForEach-Object { $af=$_|Get-NetFirewallAddressFilter; @{name=$_.IPsecRuleName;inbound=$_.InboundSecurity.ToString();outbound=$_.OutboundSecurity.ToString();requireAuthorization=[bool]$_.RequireAuthorization;remoteAddress=(@($af.RemoteAddress) -join ',')} })
  $setting=Get-NetFirewallSetting -PolicyStore $store -ErrorAction SilentlyContinue
  $perm=@(Get-GPPermission -Guid $g.Id -All | Where-Object { $_.Permission -eq 'GpoApply' } | ForEach-Object { $_.Trustee.Name })
  $acl=Get-Acl -Path ('AD:\'+$g.Path)
  $deny=@($acl.Access | Where-Object { $_.AccessControlType -eq 'Deny' -and $_.ObjectType -eq $apply } | ForEach-Object { $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value })
  $gpos+=@{name=$name;exists=$true;marker=$g.Description;version=[int]$g.Computer.DSVersion;linked=[bool]$links[$name];rules=$rules;authorizationSddl=[string]$setting.RemoteMachineTransportAuthorizationList;exemptions=[string]$setting.Exemptions;applyGroups=$perm;denySids=$deny}
}
$external=@()
foreach($g in (Get-GPO -All | Where-Object { $_.DisplayName -notin @($spec.gpos) })){
  $n=@(Get-NetIPsecRule -PolicyStore ($spec.domain+'\'+$g.DisplayName) -ErrorAction SilentlyContinue).Count
  if($n -gt 0){ $report=[xml](Get-GPOReport -Guid $g.Id -ReportType Xml); $external+=@{name=$g.DisplayName;ruleCount=$n;links=@($report.GPO.LinksTo | ForEach-Object { $_.SOMPath })} }
}
@{gpos=$gpos;external=$external} | ConvertTo-Json -Compress -Depth 6
""";
    const string RemotePreamble = """`);
fs.writeFileSync('app/IpsecStudio/Deployment.cs', s);

let l = read('app/IpsecStudio/L10n.cs');
l = rep(l, `        // simulator`, `        ["drift.missing"] = ("GPO não existe no AD.", "GPO missing in AD."),
        ["drift.marker"] = ("Marcador esperado '{0}', encontrado '{1}'.", "Expected marker '{0}', found '{1}'."),
        ["drift.unlinked"] = ("GPO sem vínculo habilitado no destino.", "GPO not linked (enabled) at the target."),
        ["drift.ruleMissing"] = ("Regra {0} ausente.", "Rule {0} missing."),
        ["drift.ruleExtra"] = ("Regra inesperada {0}.", "Unexpected rule {0}."),
        ["drift.field"] = ("{0}: {1} esperado '{2}', encontrado '{3}'.", "{0}: {1} expected '{2}', found '{3}'."),
        ["drift.external"] = ("GPO externa {0} com {1} regra(s) IPsec", "External GPO {0} with {1} IPsec rule(s)"),
        ["drift.blocked"] = ("Drift detectado; reconheça para continuar: {0}", "Drift detected; acknowledge to continue: {0}"),
        // simulator`);
fs.writeFileSync('app/IpsecStudio/L10n.cs', l);

let p = read('app/IpsecStudio/Program.cs');
p = rep(p, `app.MapPost("/api/wave",`, `app.MapGet("/api/plans/{id}/drift", async (string id, PlanRepository repository, Deployment deployment) => { var saved = await repository.Get(id) ?? throw new ValidationException(L.T("api.planNotFound")); return await deployment.Drift(saved.Id, saved.Version); });
app.MapPost("/api/wave",`);
fs.writeFileSync('app/IpsecStudio/Program.cs', p);
console.log('patched');
