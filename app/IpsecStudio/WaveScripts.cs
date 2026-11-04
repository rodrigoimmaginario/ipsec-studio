using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IpsecStudio;

// Ownership is persisted BEFORE/AFTER each AD mutation. An interrupted mutation is ambiguous: refuse to guess on retry/undo.
internal static class WaveScripts {
    public static WaveCompilation Compile(SavedPlan plan, string waveId, string? journalPath) {
        PlanRules.Assess(plan.Input, plan.Inventory);
        var wave = plan.Input.Waves.FirstOrDefault(w => w.Id == waveId) ?? throw new ValidationException(L.T("waves.notFound"));
        var map = plan.Inventory.Objects.ToDictionary(o => o.Id);
        journalPath ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "IpsecStudio", "waves", $"{plan.Id}-v{plan.Version}-{waveId}.json");
        var pairs = wave.Members.SelectMany(m => m.ComputerIds.Select(c => new { g = map[m.GroupId].Sid!, c = map[c].Sid! })).Distinct().ToArray();
        var scope=Authorization.Scopes(plan.Input,plan.Inventory);
        var spec = JsonSerializer.Serialize(new { journalPath, pairs, scopeKnown=wave.Members.SelectMany(m=>m.ComputerIds).All(id=>scope[id]==true), errors = new { unproven=L.T("sim.scopeUnknown"), interrupted=L.T("waves.interrupted"), missing=L.T("waves.noJournal"), scope=L.T("waves.journalScope") } });
        var head = Compiler.MembershipPreamble.Replace("__PINPDC__", Compiler.PinPdc).Replace("__DOMAINSID__", plan.Inventory.DomainSid).Replace("__MARKER__", $"IpsecStudio plan {plan.Id} v{plan.Version} wave {wave.Id}") + "\n" + Journal.Replace("__DATA__", spec);
        var tail = "\n" + Compiler.WaitMembership.Replace("__REPLWAIT__", plan.Input.Settings.ReplicationWaitSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)) + "\n@{marker=$marker;changed=$changed;replication=$repl;journal=$spec.journalPath}|ConvertTo-Json -Compress\n} finally { $lock.Dispose(); Remove-Item $lockPath -Force }";
        var apply = head + "\n" + Add + tail;
        var rollback = head + "\n" + Remove + tail;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apply + "\n--\n" + rollback))).ToLowerInvariant();
        return new(wave.Id, wave.Name, wave.Members.SelectMany(m => m.ComputerIds).Distinct().ToArray(), apply, rollback, hash);
    }
    const string Journal = """
$spec=ConvertFrom-Json @'
__DATA__
'@
$dir=Split-Path $spec.journalPath -Parent
New-Item -ItemType Directory -Path $dir -Force | Out-Null
$lockPath=$spec.journalPath+'.lock'
$lock=[IO.File]::Open($lockPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try {
$journal=@()
if(Test-Path $spec.journalPath){
  $loaded=Get-Content $spec.journalPath -Raw | ConvertFrom-Json
  $journal=@(foreach($item in $loaded){ [pscustomobject]@{g=[string]$item.g;c=[string]$item.c;state=[string]$item.state} })
}
if(@($journal | Where-Object { $_.state -notin @('added','removed') }).Count){ throw $spec.errors.interrupted }
if(@($journal | Where-Object { $e=$_; -not @($spec.pairs | Where-Object { $_.g -eq $e.g -and $_.c -eq $e.c }).Count }).Count){ throw $spec.errors.scope }
function Save-Journal {
  $tmp=$spec.journalPath+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
  $rows=@($script:journal | ForEach-Object { [ordered]@{g=$_.g;c=$_.c;state=$_.state} })
  $bytes=[Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -InputObject $rows -Depth 4 -Compress))
  $s=[IO.File]::Open($tmp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
  try { $s.Write($bytes,0,$bytes.Length); $s.Flush($true) } finally { $s.Dispose() }
  # A scanner/reader may briefly hold the destination without FILE_SHARE_DELETE. Retry the same flushed atomic commit;
  # never delete the original or fall back to a non-atomic overwrite. Persistent/ambiguous failure remains inconclusive.
  for($attempt=1;$attempt -le 20;$attempt++){
    try {
      if(Test-Path $spec.journalPath){[IO.File]::Replace($tmp,$spec.journalPath,[NullString]::Value)}else{[IO.File]::Move($tmp,$spec.journalPath)}
      break
    }catch{
      $io=($_.Exception -is [IO.IOException] -or $_.Exception.InnerException -is [IO.IOException])
      if(-not $io -or -not (Test-Path $tmp) -or $attempt -eq 20){throw}
      Start-Sleep -Milliseconds 100
    }
  }
}
""";
    const string Add = """
if(-not $spec.scopeKnown){throw $spec.errors.unproven}
# Existing members are never claimed by this wave. A completed prior add remains owned on an idempotent retry.
foreach($p in @($spec.pairs)){
  $entry=@($journal | Where-Object { $_.g -eq $p.g -and $_.c -eq $p.c } | Select-Object -Last 1)
  $present=Test-StudioMembership $p.g $p.c
  if($present){ continue }
  $journal=@($journal | Where-Object { -not ($_.g -eq $p.g -and $_.c -eq $p.c) })
  $e=[pscustomobject]@{g=$p.g;c=$p.c;state='pending'}; $journal+= $e; Save-Journal
  Add-ADGroupMember -Identity $p.g -Members $p.c -DisablePermissiveModify
  $e.state='added'; Save-Journal; $changed++
}
# Also persist an empty journal: a successful no-op does not own preexisting members.
Save-Journal
$expect=@($spec.pairs | ForEach-Object { @{g=$_.g;c=$_.c;present=$true} })
""";
    const string Remove = """
if(-not (Test-Path $spec.journalPath)){ throw $spec.errors.missing }
$owned=@($journal | Where-Object { $_.state -eq 'added' })
foreach($e in $owned){
  $e.state='removing'; Save-Journal
  if(Test-StudioMembership $e.g $e.c){
    Remove-ADGroupMember -Identity $e.g -Members $e.c -Confirm:$false; $changed++
  }
  $e.state='removed'; Save-Journal
}
$expect=@($owned | ForEach-Object { @{g=$_.g;c=$_.c;present=$false} })
""";
}
