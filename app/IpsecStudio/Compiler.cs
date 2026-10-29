using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IpsecStudio;

// Translates a saved plan version into GPOs with native connection security rules (GroupPolicy + NetSecurity cmdlets).
// The scripts are constant code plus one JSON literal built only from validated, typed values. No free text reaches PowerShell.
public static partial class Compiler {
    [GeneratedRegex(@"[^A-Za-z0-9]")] private static partial Regex NonAlnum();
    public static Compilation Compile(SavedPlan plan) {
        var input = plan.Input; var snapshot = plan.Inventory;
        var a = PlanRules.Assess(input, snapshot); // never trust the stored assessment
        if (a.Hosts.Any(h => h.Conflicts.Length > 0)) throw new ValidationException(L.T("compile.conflicts", string.Join("; ", a.Hosts.Where(h => h.Conflicts.Length > 0).Select(h => h.Name))));
        var map = snapshot.Objects.ToDictionary(o => o.Id);
        var baseGroup = input.BaseGroupId is null ? PlanRules.DomainComputers(snapshot)! : map[input.BaseGroupId];
        string Sid(string id) => PlanRules.ValidSid(map[id].Sid) ? map[id].Sid! : throw new ValidationException(L.T("compile.noSid", map[id].Name));
        var exclusionSid = input.ExclusionGroupId is null ? null : Sid(input.ExclusionGroupId);
        string Sddl(IEnumerable<string> allow, bool deny) {
            var aces = new StringBuilder("O:LSD:");
            if (deny && exclusionSid is not null) aces.Append($"(D;;CC;;;{exclusionSid})");
            foreach (var s in allow.Distinct()) aces.Append($"(A;;CC;;;{s})");
            return aces.ToString();
        }
        var (bIn, bOut) = PlanRules.Modes(input, "base");
        var (sIn, sOut) = PlanRules.Modes(input, "restricted");
        var otherGroups = new List<string>();
        var gpos = new List<GpoSpec>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Name(string suffix) { var n = "IpsecStudio-" + suffix; if (!names.Add(n)) throw new ValidationException(L.T("compile.dupName", n)); return n; }
        foreach (var r in input.Restricted.Where(r => r.Included)) {
            var slug = NonAlnum().Replace(r.Name, ""); if (slug.Length == 0) throw new ValidationException(L.T("compile.slug", r.Name));
            var allow = new List<string> { Sid(r.ProtectedGroupId) };
            allow.Add(r.AccessGroupId is null ? baseGroup.Sid! : Sid(r.AccessGroupId));
            if (input.Settings.ManagementGroupId is not null) allow.Add(Sid(input.Settings.ManagementGroupId));
            gpos.Add(new(Name("Restricted-" + slug[..Math.Min(24, slug.Length)]), "restricted", map[r.ProtectedGroupId].Name, Sid(r.ProtectedGroupId), [], sIn, sOut, r.Encryption, true, Sddl(allow, true), a.Hosts.Where(h => h.RestrictedId == r.Id).Select(h => h.ComputerId).ToArray()));
            otherGroups.Add(Sid(r.ProtectedGroupId));
        }
        if (input.BoundaryGroupId is not null) {
            gpos.Add(new(Name("Boundary"), "boundary", map[input.BoundaryGroupId].Name, Sid(input.BoundaryGroupId), [], "Request", "Request", false, false, null, a.Hosts.Where(h => h.Profile == "boundary").Select(h => h.ComputerId).ToArray()));
            otherGroups.Add(Sid(input.BoundaryGroupId));
        }
        if (input.Settings.ManagementGroupId is not null) {
            gpos.Add(new(Name("Management"), "management", map[input.Settings.ManagementGroupId].Name, Sid(input.Settings.ManagementGroupId), [], bIn, bOut, false, true, Sddl([baseGroup.Sid!], false), a.Hosts.Where(h => h.Profile == "management").Select(h => h.ComputerId).ToArray()));
            otherGroups.Add(Sid(input.Settings.ManagementGroupId));
        }
        if (input.ExclusionGroupId is not null) {
            var allow = input.Settings.ManagementGroupId is null ? Array.Empty<string>() : [Sid(input.Settings.ManagementGroupId)];
            gpos.Add(new(Name("Quarantine"), "quarantine", map[input.ExclusionGroupId].Name, exclusionSid!, [], sIn, sOut, false, true, Sddl(allow, false), a.Hosts.Where(h => h.Profile == "quarantine").Select(h => h.ComputerId).ToArray()));
            otherGroups.Add(exclusionSid!);
        }
        var denyBase = otherGroups.ToList();
        if (input.NoPolicyGroupId is not null) denyBase.Add(Sid(input.NoPolicyGroupId));
        gpos.Insert(0, new(Name("Base"), "base", baseGroup.Name, baseGroup.Sid!, denyBase.Distinct().ToArray(), bIn, bOut, false, true, Sddl([baseGroup.Sid!], true), a.Hosts.Where(h => h.Profile == "base").Select(h => h.ComputerId).ToArray()));
        // Every non-base GPO denies the no-policy group; every non-quarantine GPO denies the exclusion group (quarantine wins over any other profile).
        var np = input.NoPolicyGroupId is null ? null : Sid(input.NoPolicyGroupId);
        gpos = gpos.Select(g => g.Profile == "base" ? g : g with { DenyGroupSids = [.. g.DenyGroupSids, .. (np is null ? [] : new[] { np }), .. (exclusionSid is null || g.Profile == "quarantine" ? [] : new[] { exclusionSid })] }).ToList();
        foreach (var g in gpos) if (!PlanRules.ValidGroupName(g.ApplyGroup)) throw new ValidationException(L.T("compile.groupName", g.ApplyGroup));
        var exempt = a.ExemptAddresses.Concat(input.Settings.ExemptDomainControllers ? a.DomainControllerAddresses : []).Distinct().Order(StringComparer.Ordinal).ToArray();
        var marker = $"IpsecStudio plan {plan.Id} v{plan.Version}";
        var spec = new {
            domain = snapshot.Domain, domainSid = snapshot.DomainSid, marker, linkTarget = input.Settings.LinkTarget,
            secureSubnets = input.Settings.SecureSubnets, exemptAddresses = exempt, exemptIcmp = input.Settings.ExemptIcmp,
            crypto = input.Settings.Crypto,
            gpos = gpos.Select(g => new { g.Name, g.Profile, g.ApplyGroup, g.DenyGroupSids, g.Inbound, g.Outbound, g.Encryption, g.RequireAuthorization, g.AuthorizationSddl })
        };
        var json = JsonSerializer.Serialize(spec, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (json.Contains('\n') || json.Contains("'@")) throw new ValidationException(L.T("compile.spec"));
        var apply = ApplyTemplate.Replace("__PINPDC__", PinPdc).Replace("__SPEC__", json);
        var rollback = RollbackTemplate.Replace("__PINPDC__", PinPdc).Replace("__SPEC__", json);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apply + "\n--\n" + rollback))).ToLowerInvariant();
        return new(plan.Id, plan.Version, snapshot.Id, snapshot.Domain, snapshot.DomainSid, marker, gpos.ToArray(), exempt, apply, rollback, hash) { Crypto = input.Settings.Crypto, ExemptIcmp = input.Settings.ExemptIcmp };
    }
    // Wave: membership changes only. Everything is addressed by SID; names never reach PowerShell.
    public static WaveCompilation CompileWave(SavedPlan plan, string waveId) {
        var a = PlanRules.Assess(plan.Input, plan.Inventory);
        var wave = plan.Input.Waves.FirstOrDefault(w => w.Id == waveId) ?? throw new ValidationException(L.T("waves.notFound"));
        var map = plan.Inventory.Objects.ToDictionary(o => o.Id);
        var lines = new List<string>(); var undo = new List<string>(); var computers = new List<string>();
        foreach (var m in wave.Members) {
            var gsid = map[m.GroupId].Sid!;
            foreach (var cid in m.ComputerIds) {
                var csid = map[cid].Sid!; computers.Add(cid);
                lines.Add($"if(-not (Get-ADGroupMember -Identity '{gsid}' | Where-Object {{ $_.SID.Value -eq '{csid}' }})){{Add-ADGroupMember -Identity '{gsid}' -Members '{csid}'; $changed++}}");
                undo.Add($"if(Get-ADGroupMember -Identity '{gsid}' | Where-Object {{ $_.SID.Value -eq '{csid}' }}){{Remove-ADGroupMember -Identity '{gsid}' -Members '{csid}' -Confirm:$false; $changed++}}");
            }
        }
        var head = MembershipPreamble.Replace("__PINPDC__", PinPdc).Replace("__DOMAINSID__", plan.Inventory.DomainSid).Replace("__MARKER__", $"IpsecStudio plan {plan.Id} v{plan.Version} wave {wave.Id}");
        var apply = head + NL + string.Join(NL, lines) + NL + "@{marker=$marker;changed=$changed}|ConvertTo-Json -Compress";
        var rollback = head + NL + string.Join(NL, undo) + NL + "@{marker=$marker;changed=$changed}|ConvertTo-Json -Compress";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apply + "\n--\n" + rollback))).ToLowerInvariant();
        return new(wave.Id, wave.Name, computers.Distinct().ToArray(), apply, rollback, hash);
    }
    // New security groups, created before they can be referenced (they need a SID in the inventory).
    public static GroupsCompilation CompileGroups(SavedPlan plan) {
        PlanRules.Assess(plan.Input, plan.Inventory);
        if (plan.Input.NewGroups.Length == 0) throw new ValidationException(L.T("deploy.noGroups"));
        var container = plan.Input.Settings.GroupContainer ?? "CN=Users," + plan.Inventory.DomainDn;
        if (!PlanRules.ValidContainerDn(container)) throw new ValidationException(L.T("groups.container"));
        var lines = plan.Input.NewGroups.Select(g => $"if(-not (Get-ADGroup -Filter \"Name -eq '{g.Name}'\")){{New-ADGroup -Name '{g.Name}' -GroupScope Global -GroupCategory Security -Path '{container}' -Description '{g.Description}'; $changed++}}");
        var head = MembershipPreamble.Replace("__PINPDC__", PinPdc).Replace("__DOMAINSID__", plan.Inventory.DomainSid).Replace("__MARKER__", $"IpsecStudio plan {plan.Id} v{plan.Version} groups");
        var apply = head + NL + string.Join(NL, lines) + NL + "@{marker=$marker;changed=$changed}|ConvertTo-Json -Compress";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apply))).ToLowerInvariant();
        return new(plan.Input.NewGroups.Select(g => g.Name).ToArray(), container, apply, hash);
    }
    // With several DCs every write and the reads that follow it must hit the same DC: New-GPO writes on the PDC emulator, so the
    // AD and GroupPolicy cmdlets and the AD drive used for ACLs are pinned to it (lab, CV4.DS5: dc2 had not replicated the new GPO).
    public const string PinPdc = """
$pdc=$domain.PDCEmulator
$PSDefaultParameterValues['*-AD*:Server']=$pdc
$PSDefaultParameterValues['*-GP*:Server']=$pdc
if(-not (Get-PSDrive IpsecPdc -ErrorAction SilentlyContinue)){ New-PSDrive -Name IpsecPdc -PSProvider ActiveDirectory -Root '//RootDSE/' -Server $pdc | Out-Null }
""";
    const string MembershipPreamble = """
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
Import-Module ActiveDirectory
$domain=Get-ADDomain
if($domain.DomainSID.Value -ne '__DOMAINSID__'){throw "Wrong domain: $($domain.DNSRoot)"}
__PINPDC__
$marker='__MARKER__'
$changed=0
""";
    const string Preamble = """
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$spec=ConvertFrom-Json @'
__SPEC__
'@
Import-Module GroupPolicy
Import-Module ActiveDirectory
$domain=Get-ADDomain
if($domain.DNSRoot -ne $spec.domain -or $domain.DomainSID.Value -ne $spec.domainSid){throw "Wrong domain: $($domain.DNSRoot)"}
__PINPDC__
$target=if($spec.linkTarget -eq 'domain'){$domain.DistinguishedName}else{$spec.linkTarget}
function Get-StudioGpo([string]$name){$g=Get-GPO -Name $name -ErrorAction SilentlyContinue;if($g -and $g.Description -ne $spec.marker){throw "GPO $name exists but belongs to another revision: $($g.Description)"};return $g}
# Hosts refresh from any DC: wait (max 180 s) until every reachable DC shows the Studio GPOs present with AD = SYSVOL version
# (after apply) or absent (after rollback). Unreachable DCs are reported, not waited for.
function Wait-StudioReplication([bool]$Present){
  $deadline=(Get-Date).AddSeconds(180); $state=[ordered]@{}
  foreach($dc in @(Get-ADDomainController -Filter * | ForEach-Object { $_.HostName })){
    if(-not (Test-NetConnection $dc -Port 389 -InformationLevel Quiet -WarningAction SilentlyContinue)){ $state[$dc]='unreachable'; continue }
    $ok=$false
    while(-not $ok -and (Get-Date) -lt $deadline){
      $ok=$true
      foreach($n in @($spec.gpos.name)){
        $g=Get-GPO -Name $n -Server $dc -ErrorAction SilentlyContinue
        if($Present){ if(-not ($g -and $g.Description -eq $spec.marker -and $g.Computer.DSVersion -eq $g.Computer.SysvolVersion)){ $ok=$false } } elseif($g){ $ok=$false }
      }
      if(-not $ok){ Start-Sleep -Seconds 5 }
    }
    $state[$dc]=if($ok){'ok'}else{'timeout'}
  }
  return $state
}
""";
    const string NL = "\n";
    const string ApplyTemplate = Preamble + NL + """
function Deny-Apply([guid]$id,[string]$sid){
  $path='IpsecPdc:\CN='+$id.ToString('B').ToUpper()+',CN=Policies,CN=System,'+$domain.DistinguishedName
  $acl=Get-Acl -Path $path
  $rule=New-Object System.DirectoryServices.ActiveDirectoryAccessRule([System.Security.Principal.SecurityIdentifier]$sid,[System.DirectoryServices.ActiveDirectoryRights]::ExtendedRight,[System.Security.AccessControl.AccessControlType]::Deny,[guid]'edacfd8f-ffb3-11d1-b41d-00a0c968f939')
  $acl.AddAccessRule($rule)
  Set-Acl -Path $path -AclObject $acl
}
foreach($g in $spec.gpos){if(Get-GPO -Name $g.name -ErrorAction SilentlyContinue){throw "GPO already exists: $($g.name). Roll back first."}}
$created=@()
try{
  foreach($g in $spec.gpos){
    $gpo=New-GPO -Name $g.name -Comment $spec.marker
    $created+=$g.name
    $store=$spec.domain+'\'+$g.name
    Set-GPPermission -Guid $gpo.Id -TargetName 'Authenticated Users' -TargetType Group -PermissionLevel GpoRead -Replace | Out-Null
    Set-GPPermission -Guid $gpo.Id -TargetName $g.applyGroup -TargetType Group -PermissionLevel GpoApply | Out-Null
    foreach($sid in @($g.denyGroupSids)){Deny-Apply $gpo.Id $sid}
    # IPsec settings go into the GPO through a session opened on the PDC, where New-GPO created it (no replication wait).
    $gs=Open-NetGPO -PolicyStore $store -DomainController $pdc
    $mm=New-NetIPsecMainModeCryptoProposal -Encryption $spec.crypto.mainModeEncryption -Hash $spec.crypto.mainModeHash -KeyExchange $spec.crypto.keyExchange
    New-NetIPsecMainModeCryptoSet -GPOSession $gs -DisplayName 'IpsecStudio main mode' -Name 'IpsecStudio-MM' -Proposal $mm -MaxMinutes $spec.crypto.mainModeLifetimeMinutes -Default | Out-Null
    $auth=New-NetIPsecAuthProposal -Machine -Kerberos
    New-NetIPsecPhase1AuthSet -GPOSession $gs -DisplayName 'IpsecStudio Kerberos machine' -Name 'IpsecStudio-P1' -Proposal $auth | Out-Null
    $enc=New-NetIPsecQuickModeCryptoProposal -Encapsulation ESP -Encryption $spec.crypto.quickModeEncryption -ESPHash $spec.crypto.quickModeIntegrity -MaxMinutes $spec.crypto.quickModeLifetimeMinutes
    $plain=New-NetIPsecQuickModeCryptoProposal -Encapsulation ESP -Encryption None -ESPHash $spec.crypto.quickModeIntegrity -MaxMinutes $spec.crypto.quickModeLifetimeMinutes
    $proposals=if($g.encryption){@($enc)}else{@($plain,$enc)}
    New-NetIPsecQuickModeCryptoSet -GPOSession $gs -DisplayName 'IpsecStudio quick mode' -Name 'IpsecStudio-QM' -Proposal $proposals | Out-Null
    if(@($spec.exemptAddresses).Count -gt 0){New-NetIPsecRule -GPOSession $gs -DisplayName 'IpsecStudio exempt peers' -IPsecRuleName 'IpsecStudio-Exempt' -Mode Transport -Protocol Any -RemoteAddress @($spec.exemptAddresses) -InboundSecurity None -OutboundSecurity None | Out-Null}
    $remote=if(@($spec.secureSubnets).Count -gt 0){@($spec.secureSubnets)}else{'Any'}
    New-NetIPsecRule -GPOSession $gs -DisplayName ('IpsecStudio '+$g.profile) -IPsecRuleName 'IpsecStudio-Main' -Mode Transport -Protocol Any -RemoteAddress $remote -InboundSecurity $g.inbound -OutboundSecurity $g.outbound -Phase1AuthSet 'IpsecStudio-P1' -QuickModeCryptoSet 'IpsecStudio-QM' -RequireAuthorization ([bool]$g.requireAuthorization) | Out-Null
    if($g.authorizationSddl){Set-NetFirewallSetting -GPOSession $gs -RemoteMachineTransportAuthorizationList $g.authorizationSddl}
    if($spec.exemptIcmp){Set-NetFirewallSetting -GPOSession $gs -Exemptions Icmp}
    Save-NetGPO -GPOSession $gs
  }
  foreach($g in $spec.gpos){New-GPLink -Name $g.name -Target $target -LinkEnabled Yes | Out-Null}
  $links=@((Get-GPInheritance -Target $target).GpoLinks | Where-Object {$_.DisplayName -in @($spec.gpos.name)} | Select-Object DisplayName,Enabled,Order)
  $replication=Wait-StudioReplication -Present $true
  @{marker=$spec.marker;target=$target;gpos=@($spec.gpos | ForEach-Object {$x=Get-GPO -Name $_.name;@{name=$_.name;guid=$x.Id.ToString();profile=$_.profile}});links=$links;replication=$replication} | ConvertTo-Json -Compress -Depth 6
}catch{
  foreach($name in $created){$g=Get-GPO -Name $name -ErrorAction SilentlyContinue;if($g -and $g.Description -eq $spec.marker){Remove-GPLink -Name $name -Target $target -ErrorAction SilentlyContinue | Out-Null;Remove-GPO -Name $name | Out-Null}}
  throw
}
""";
    const string RollbackTemplate = Preamble + NL + """
$removed=@()
foreach($g in $spec.gpos){
  $x=Get-StudioGpo $g.name
  if(-not $x){continue}
  Remove-GPLink -Name $g.name -Target $target -ErrorAction SilentlyContinue | Out-Null
  Remove-GPO -Guid $x.Id | Out-Null
  $removed+=$g.name
}
$remaining=@((Get-GPInheritance -Target $target).GpoLinks | Where-Object {$_.DisplayName -like 'IpsecStudio-*'} | Select-Object DisplayName,Enabled)
$replication=Wait-StudioReplication -Present $false
@{marker=$spec.marker;target=$target;removed=$removed;remainingStudioLinks=$remaining;replication=$replication} | ConvertTo-Json -Compress -Depth 5
""";
}
