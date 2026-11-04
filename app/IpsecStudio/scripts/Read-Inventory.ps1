param([Parameter(Mandatory=$true)][string]$Server,[Parameter(Mandatory=$true)][string]$ExpectedDomain)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
try {
    Import-Module ActiveDirectory -ErrorAction Stop
    # Server = the domain name: discover a DC that answers ADWS now, instead of a cached one that may be down (lab, CV4.DS5: dc1 off).
    if($Server -eq $ExpectedDomain){ $Server=@((Get-ADDomainController -Discover -DomainName $ExpectedDomain -Service ADWS -ForceDiscover -ErrorAction Stop).HostName)[0] }
    $domain=Get-ADDomain -Server $Server
    if($domain.DNSRoot -ne $ExpectedDomain){throw 'O servidor consultado pertence a outro dominio.'}
    $base=$domain.DistinguishedName
    $computers=@(Get-ADComputer -Filter * -Server $Server -Properties OperatingSystem,IPv4Address,userAccountControl,PrimaryGroupID,SID)
    $users=@(Get-ADUser -Filter * -Server $Server -Properties PrimaryGroupID)
    $groups=@(Get-ADGroup -Filter * -Server $Server -Properties Member,GroupCategory,SID)
    $ous=@(Get-ADOrganizationalUnit -Filter * -Server $Server -Properties gPLink,gPOptions)
    $gpos=@(Get-ADObject -LDAPFilter '(objectClass=groupPolicyContainer)' -SearchBase "CN=Policies,CN=System,$base" -Server $Server -Properties displayName,gPCFileSysPath)
    $domainObject=Get-ADObject -Identity $base -Server $Server -Properties gPLink
    $records=[Collections.Generic.List[object]]::new()
    $map=@{}
    foreach($obj in @($computers)+@($users)+@($groups)+@($ous)+@($gpos)){$map[$obj.DistinguishedName]=[string]$obj.ObjectGUID}
    function Record($obj,[string]$kind){return [ordered]@{id=[string]$obj.ObjectGUID;name=[string]$obj.Name;kind=$kind;dn=[string]$obj.DistinguishedName;sid=$(if($obj.SID){[string]$obj.SID.Value}else{$null});parentDn=([string]$obj.DistinguishedName -replace '^(?:[^,\\]|\\.)*,','');os=$null;ip=$null;enabled=$null;memberIds=@();unresolvedMemberCount=0;policyPath=$null;policyLinks=$null;policyFileReadable=$false}}
    foreach($obj in $computers){
        $kind=if(($obj.userAccountControl -band 8192) -ne 0){'dc'}elseif($obj.OperatingSystem -like '*Server*'){'server'}elseif($obj.OperatingSystem){'workstation'}else{'computer'}
        $r=Record $obj $kind;$r.os=$obj.OperatingSystem;$r.ip=$obj.IPv4Address;$r.enabled=[bool]$obj.Enabled;$records.Add($r)
    }
    foreach($obj in $users){$r=Record $obj 'user';$r.enabled=[bool]$obj.Enabled;$records.Add($r)}
    # Primary-group members are indexed once; scanning every account for each group made large inventories quadratic.
    $primary=@{}
    foreach($member in @($computers)+@($users)){
        $key="$($domain.DomainSID.Value)-$($member.PrimaryGroupID)"
        if(-not $primary.ContainsKey($key)){ $primary[$key]=[Collections.Generic.List[string]]::new() }
        $primary[$key].Add([string]$member.ObjectGUID)
    }
    foreach($obj in $groups){
        $r=Record $obj $(if($obj.GroupCategory -eq 'Security'){'group'}else{'distributionGroup'})
        $ids=[Collections.Generic.HashSet[string]]::new()
        foreach($dn in @($obj.Member)){if($map.ContainsKey($dn)){[void]$ids.Add($map[$dn])}else{$r.unresolvedMemberCount++}}
        # AD does not list primary-group members in the member attribute. Include them explicitly.
        if($primary.ContainsKey($obj.SID.Value)){ foreach($id in $primary[$obj.SID.Value]){ [void]$ids.Add($id) } }
        $r.memberIds=@($ids | Sort-Object);$records.Add($r)
    }
    foreach($obj in $ous){$r=Record $obj 'ou';$r.policyLinks=[string]$obj.gPLink;$r.blocksInheritance=[bool]([int](@($obj.gPOptions)[0]) -band 1);$records.Add($r)}
    $warnings=[Collections.Generic.List[string]]::new()
    foreach($obj in $gpos){
        $r=Record $obj 'gpo';$r.name=[string]$obj.displayName;$r.policyPath=[string]$obj.gPCFileSysPath
        # Only read SYSVOL from this exact domain. No arbitrary UNC path supplied by an AD attribute.
        $expectedPrefix='\\'+$domain.DNSRoot+'\SYSVOL\'+$domain.DNSRoot+'\Policies\'
        if($r.policyPath.StartsWith($expectedPrefix,[StringComparison]::OrdinalIgnoreCase)){
            try{[void][IO.File]::ReadAllText((Join-Path $r.policyPath 'GPT.INI'));$r.policyFileReadable=$true}catch{$warnings.Add('SYSVOL nao legivel para GPO: '+$r.name)}
        }else{$warnings.Add('Caminho SYSVOL fora do dominio esperado: '+$r.name)}
        $records.Add($r)
    }
    foreach($r in $records){if($r.unresolvedMemberCount -gt 0){$warnings.Add("Grupo $($r.name): $($r.unresolvedMemberCount) membros nao resolvidos neste dominio.")}}
    $result=@{id='';capturedAt=(Get-Date).ToUniversalTime().ToString('o');domain=$domain.DNSRoot;domainSid=$domain.DomainSID.Value;domainDn=$domain.DistinguishedName;domainPolicyLinks=[string]$domainObject.gPLink;server=$Server;identity=[Security.Principal.WindowsIdentity]::GetCurrent().Name;objects=@($records);warnings=@($warnings)}
    $result | ConvertTo-Json -Depth 10 -Compress
    exit 0
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
