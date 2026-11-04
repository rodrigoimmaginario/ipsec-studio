using IpsecStudio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IpsecStudio.Tests;

public class ReliabilityTests : IDisposable {
    readonly LabFixture f = new();
    readonly string root = Path.Combine(Path.GetTempPath(), "ipsec-reliable-" + LabFixture.Id());
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    sealed class Runner : IScriptRunner {
        public readonly List<string> Scripts = [];
        public Func<string,(bool,string)> Reply = script=>script.Contains("gpupdate") ? (true,"[]") : ScriptReplies.Default(script);
        public Task<(bool Ok, string Output)> Run(string script, TimeSpan timeout) { Scripts.Add(script); return Task.FromResult(Reply(script)); }
    }
    [Fact]
    public async Task Rollback_can_target_the_applied_revision_after_a_new_revision_is_saved() {
        var repo = new PlanRepository(root);
        var v1 = await repo.Save(null, f.Plan, f.Snapshot, PlanRules.Assess(f.Plan, f.Snapshot), "operator");
        await repo.Save(v1.Id, f.Plan with { ExpectedVersion = 1, Name = "Edited" }, f.Snapshot, v1.Assessment, "operator");
        var runner = new Runner();
        var d = new Deployment(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Deployment:Enabled"] = "true" }).Build(), repo, runner, NullLogger<Deployment>.Instance);
        var r = await d.Execute(new(v1.Id, 1, Compiler.Compile(v1).Sha256, "rollback"), "operator");
        Assert.True(r.Succeeded);
        Assert.Contains(runner.Scripts, s => s.Contains("Remove-GPO") && s.Contains($"IpsecStudio plan {v1.Id} v1"));
        Assert.DoesNotContain(runner.Scripts, s => s.Contains($"IpsecStudio plan {v1.Id} v2"));
    }
    [WindowsFact]
    public async Task Real_PowerShell_journal_preserves_preexisting_members_and_undoes_only_new_members() {
        Directory.CreateDirectory(root);
        var wave = new Wave { Id = LabFixture.Id(), Name = "Pilot", Members = [new() { GroupId = f.SqlClients.Id, ComputerIds = [f.App1.Id, f.ClientNo.Id] }] };
        var c = Compiler.CompileWave(f.Saved(f.Plan with { Waves = [wave] }), wave.Id, Path.Combine(root, "journal.json"));
        var apply = Path.Combine(root, "apply.ps1"); var undo = Path.Combine(root, "undo.ps1");
        await File.WriteAllTextAsync(apply, c.ApplyScript); await File.WriteAllTextAsync(undo, c.RollbackScript);
        var script = $$$"""
function Import-Module {}
function Get-ADDomain { [pscustomobject]@{ DomainSID=[pscustomobject]@{Value='{{{LabFixture.DomainSid}}}'}; PDCEmulator='dc.test' } }
function Get-PSDrive { $true }
function Get-ADDomainController { @() }
function Get-ADComputer { param($Identity,$Server,$Properties) [pscustomobject]@{ SID=[pscustomobject]@{Value=$Identity};PrimaryGroupID=515;MemberOf=@(if($global:members.Contains($Identity)){'CN={{{f.SqlClients.Sid}}}'}) } }
function Get-ADGroup { param($Identity,$Server) [pscustomobject]@{DistinguishedName=('CN='+$Identity)} }
$global:members=[Collections.Generic.HashSet[string]]::new(); [void]$global:members.Add('{{{f.App1.Sid}}}')
function Get-ADGroupMember { param($Identity) foreach($s in $global:members){ [pscustomobject]@{SID=[pscustomobject]@{Value=$s}} } }
function Add-ADGroupMember { param($Identity,$Members) [void]$global:members.Add($Members) }
function Remove-ADGroupMember { param($Identity,$Members,[switch]$Confirm) [void]$global:members.Remove($Members) }
& '{{{apply}}}'
& '{{{apply}}}'
& '{{{undo}}}'
& '{{{undo}}}'
@{kept=$global:members.Contains('{{{f.App1.Sid}}}');removed=(-not $global:members.Contains('{{{f.ClientNo.Sid}}}'))}|ConvertTo-Json -Compress
""";
        var (ok, output) = await new PowerShellRunner(NullLogger<PowerShellRunner>.Instance).Run(script, TimeSpan.FromSeconds(30));
        Assert.True(ok, output + "\nJournal: " + await File.ReadAllTextAsync(Path.Combine(root, "journal.json"))); Assert.Contains("\"kept\":true", output); Assert.Contains("\"removed\":true", output);
        // Lose the AD reply AFTER a mutation: the journal remains ambiguous and neither retry nor undo may guess ownership.
        var prefix=script[..script.IndexOf("& '"+apply+"'",StringComparison.Ordinal)];
        var locked=prefix+"\nAdd-Type -TypeDefinition 'public static class JournalLock { public static void Hold(string p) { var f = new System.IO.FileStream(p, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read); System.Threading.Tasks.Task.Run(() => { System.Threading.Thread.Sleep(500); f.Dispose(); }); } }'\nfunction Add-ADGroupMember { param($Identity,$Members) [void]$global:members.Add($Members); [JournalLock]::Hold('"+Path.Combine(root,"journal.json")+"') }\n& '"+apply+"'";
        var contention=await new PowerShellRunner(NullLogger<PowerShellRunner>.Instance).Run(locked,TimeSpan.FromSeconds(30));
        Assert.True(contention.Ok,contention.Output);Assert.DoesNotContain("pending",await File.ReadAllTextAsync(Path.Combine(root,"journal.json")));
        await File.WriteAllTextAsync(Path.Combine(root,"journal.json"),"[]");
        var persistent=await new PowerShellRunner(NullLogger<PowerShellRunner>.Instance).Run(locked.Replace("Thread.Sleep(500)","Thread.Sleep(5000)"),TimeSpan.FromSeconds(30));
        Assert.False(persistent.Ok);Assert.Contains("pending",await File.ReadAllTextAsync(Path.Combine(root,"journal.json")));
        await File.WriteAllTextAsync(Path.Combine(root,"journal.json"),"[]");
        var lost=prefix+"\nfunction Add-ADGroupMember { param($Identity,$Members) [void]$global:members.Add($Members); throw 'simulated lost AD reply' }\n& '"+apply+"'";
        var failed=await new PowerShellRunner(NullLogger<PowerShellRunner>.Instance).Run(lost,TimeSpan.FromSeconds(30));
        Assert.False(failed.Ok); Assert.Contains("pending",await File.ReadAllTextAsync(Path.Combine(root,"journal.json")));
        var unsafeUndo=prefix+"\nfunction Remove-ADGroupMember { throw 'UNSAFE REMOVAL CALLED' }\n& '"+undo+"'";
        failed=await new PowerShellRunner(NullLogger<PowerShellRunner>.Instance).Run(unsafeUndo,TimeSpan.FromSeconds(30));
        Assert.False(failed.Ok); Assert.DoesNotContain("UNSAFE REMOVAL CALLED",failed.Output);
        var primaryWave=wave with {Members=[new(){GroupId=f.DomainComputers.Id,ComputerIds=[f.App1.Id]}]};
        var primaryPlan=f.Plan with {BaseGroupId=f.DomainComputers.Id,Waves=[primaryWave]};
        var primaryPath=Path.Combine(root,"primary.json");
        var primary=Compiler.CompileWave(f.Saved(primaryPlan),primaryWave.Id,primaryPath);
        var pa=Path.Combine(root,"primary-apply.ps1");var pr=Path.Combine(root,"primary-undo.ps1");
        await File.WriteAllTextAsync(pa,primary.ApplyScript);await File.WriteAllTextAsync(pr,primary.RollbackScript);
        var primaryRun=prefix+"\n$global:members.Clear(); function Add-ADGroupMember { throw 'PRIMARY ADD CALLED' }; function Remove-ADGroupMember { throw 'PRIMARY REMOVE CALLED' }\n& '"+pa+"'\n& '"+pr+"'";
        var primaryResult=await new PowerShellRunner(NullLogger<PowerShellRunner>.Instance).Run(primaryRun,TimeSpan.FromSeconds(30));
        Assert.True(primaryResult.Ok,primaryResult.Output);Assert.Equal("[]",(await File.ReadAllTextAsync(primaryPath)).Trim());
    }
    [WindowsFact]
    public async Task Every_generated_script_parses_in_Windows_PowerShell() {
        Directory.CreateDirectory(root);
        var wave=new Wave{Id=LabFixture.Id(),Name="Pilot",Members=[new(){GroupId=f.SqlClients.Id,ComputerIds=[f.ClientNo.Id]}]};
        var p=f.Plan with { Waves=[wave], NewGroups=[new(){Id=LabFixture.Id(),Name="IPSEC-New"}],Settings=f.Plan.Settings with {GroupContainer="OU=Equipe d'Operacao,DC=lab,DC=ipsec,DC=test"}};
        var saved=f.Saved(p); var c=Compiler.Compile(saved); var w=Compiler.CompileWave(saved,wave.Id);
        var source=new DirectoryInfo(AppContext.BaseDirectory);
        while(source.Parent is not null && !File.Exists(Path.Combine(source.FullName,"app/IpsecStudio/scripts/Read-Inventory.ps1"))) source=source.Parent;
        var scripts=new[]{c.ApplyScript,c.RollbackScript,w.ApplyScript,w.RollbackScript,Compiler.CompileGroups(saved).ApplyScript,Preflight.Script,Compiler.ReplicationCheck(saved),Deployment.DriftScriptText.Replace("__PINPDC__",Compiler.PinPdc),Deployment.RefreshScriptText,Deployment.VerifyScriptText,Deployment.ProbeScriptText,Deployment.CaptureStartScriptText,Deployment.CaptureStopScriptText,await File.ReadAllTextAsync(Path.Combine(source.FullName,"app/IpsecStudio/scripts/Read-Inventory.ps1"))};
        var files=scripts.Select((text,i)=>{var file=Path.Combine(root,$"script-{i}.ps1");File.WriteAllText(file,text);return file;}).ToArray();
        var parser="$files=ConvertFrom-Json @'\n"+System.Text.Json.JsonSerializer.Serialize(files)+"\n'@\n$count=0; foreach($f in $files){$tokens=$null;$errors=$null;[void][System.Management.Automation.Language.Parser]::ParseFile($f,[ref]$tokens,[ref]$errors);$count+=@($errors | Where-Object {$_}).Count}; @{errors=$count}|ConvertTo-Json -Compress";
        var (ok,output)=await new PowerShellRunner(NullLogger<PowerShellRunner>.Instance).Run(parser,TimeSpan.FromSeconds(30));
        Assert.True(ok,output); Assert.Contains("\"errors\":0",output);
    }
    [Fact]
    public async Task Historical_inventory_is_available_when_reopening_an_old_revision() {
        var repo=new PlanRepository(root); var saved=await repo.Save(null,f.Plan,f.Snapshot,PlanRules.Assess(f.Plan,f.Snapshot),"op");
        var fresh=f.Snapshot with {Id=LabFixture.Id()}; var edited=f.Plan with {InventoryId=fresh.Id,ExpectedVersion=1};
        await repo.Save(saved.Id,edited,fresh,PlanRules.Assess(edited,fresh),"op");
        Assert.NotNull(await repo.FindSnapshot(f.Snapshot.Id));
    }
    [Fact]
    public async Task Applied_artifact_is_frozen_and_its_integrity_is_checked() {
        var repo=new PlanRepository(root); var saved=await repo.Save(null,f.Plan,f.Snapshot,PlanRules.Assess(f.Plan,f.Snapshot),"op");
        var artifact=Compiler.Compile(saved); await repo.FreezeCompilation(artifact);
        await repo.AppendDeployment(new(DateTimeOffset.UtcNow,"op",saved.Id,1,artifact.Sha256,"apply",true,"{}"));
        await repo.Save(saved.Id,f.Plan with {ExpectedVersion=1,Name="new draft"},f.Snapshot,saved.Assessment,"op");
        await repo.AppendDeployment(new(DateTimeOffset.UtcNow,"op",saved.Id,2,new string('2',64),"apply",false,"failed v2 before writing"));
        var frozen=await repo.AppliedCompilation(saved.Id,1);Assert.NotNull(frozen);
        Assert.Equal(artifact.Sha256,frozen.Sha256); Assert.Equal(artifact.RollbackScript,frozen.RollbackScript);
        var file=Directory.GetFiles(Path.Combine(root,saved.Id,"compiled")).Single();
        await File.WriteAllTextAsync(file,(await File.ReadAllTextAsync(file)).Replace("Remove-GPO","Changed-GPO"));
        await Assert.ThrowsAsync<IOException>(()=>repo.ReadCompilation(saved.Id,1,artifact.Sha256));
    }
    [Fact]
    public async Task Wave_undo_can_still_reach_the_old_revision_after_GPO_rollback_and_new_edit() {
        var wave=new Wave{Id=LabFixture.Id(),Name="Pilot",Members=[new(){GroupId=f.SqlClients.Id,ComputerIds=[f.App1.Id]}]};
        var p=f.Plan with {Waves=[wave]}; var repo=new PlanRepository(root);
        var saved=await repo.Save(null,p,f.Snapshot,PlanRules.Assess(p,f.Snapshot),"op");
        await repo.AppendDeployment(new(DateTimeOffset.UtcNow,"op",saved.Id,1,"","rollback",true,"{}"));
        await repo.Save(saved.Id,p with {ExpectedVersion=1},f.Snapshot,saved.Assessment,"op");
        var runner=new Runner(); var d=new Deployment(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Deployment:Enabled"]="true"}).Build(),repo,runner,NullLogger<Deployment>.Instance);
        var r=await d.ApplyWave(new(saved.Id,1,wave.Id,"wave-rollback"),"op");
        Assert.Contains(runner.Scripts,s=>s.Contains("Remove-ADGroupMember"));
        Assert.False(r.ExpansionAllowed);
    }
    [Fact]
    public async Task Waves_require_previous_success_and_record_failure_even_after_membership_write() {
        var w1=new Wave {Id=LabFixture.Id(),Name="A",Members=[new(){GroupId=f.SqlClients.Id,ComputerIds=[f.ClientNo.Id]}]};
        var w2=w1 with {Id=LabFixture.Id(),Name="B"}; var p=f.Plan with {Waves=[w1,w2]};
        var repo=new PlanRepository(root); var saved=await repo.Save(null,p,f.Snapshot,PlanRules.Assess(p,f.Snapshot),"op");
        await repo.AppendDeployment(new(DateTimeOffset.UtcNow,"op",saved.Id,1,"","apply",true,"{}"));
        var runner=new Runner(); var d=new Deployment(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Deployment:Enabled"]="true"}).Build(),repo,runner,NullLogger<Deployment>.Instance);
        await Assert.ThrowsAsync<ConflictException>(()=>d.ApplyWave(new(saved.Id,1,w2.Id,"wave-apply","approved"),"op"));
        Assert.Empty(runner.Scripts);
        runner.Reply=script=>script.Contains("external=$external") ? (true,ScriptReplies.CleanGpos(Compiler.Compile(saved))) : script.Contains("gpupdate") ? (false,"refresh failed after mutation") : (true,"{\"replication\":{\"dc.test\":\"ok\"}}");
        var result=await d.ApplyWave(new(saved.Id,1,w1.Id,"wave-apply","approved"),"op");
        Assert.False(result.ExpansionAllowed);
        var records=await repo.ListDeployments(saved.Id);
        Assert.Contains(records,r=>r.Action=="wave-apply-started" && r.WaveId==w1.Id);
        Assert.Contains(records,r=>r.Action=="wave-apply" && r.WaveId==w1.Id && r.ExpansionAllowed==false);
    }
    [WindowsFact]
    public async Task GPO_absence_is_distinguished_from_access_errors_and_empty_reads() {
        var prefix="$ErrorActionPreference='Stop';$domain=[pscustomobject]@{PDCEmulator='dc.test'};function Get-PSDrive {$true}\n"+Compiler.PinPdc+"\n";
        foreach(var (body,ok) in new[]{("Write-Error -Message absent -ErrorId GpoWithNameNotFound -Category ObjectNotFound -ErrorAction Stop",true),("Write-Error -Message denied -ErrorId AccessDenied -Category PermissionDenied -ErrorAction Stop",false),("",false)}) {
            var script=prefix+"function Get-GPO { [CmdletBinding()]param($Name,$Server);"+body+" };Get-StudioGpoOrNull 'test'";
            var result=await new PowerShellRunner(NullLogger<PowerShellRunner>.Instance).Run(script,TimeSpan.FromSeconds(30));
            Assert.Equal(ok,result.Ok);
        }
    }
    [Theory]
    [InlineData("{}",false)]
    [InlineData("{\"replication\":{}}",false)]
    [InlineData("{\"replication\":{\"dc\":\"timeout\"}}",false)]
    [InlineData("{\"replication\":{\"dc\":\"ok\"}}",true)]
    public void Replication_requires_nonempty_positive_evidence(string output,bool confirmed) => Assert.Equal(confirmed,Deployment.ReplicationConfirmed(output));
    [Fact]
    public async Task Historical_pending_DCs_can_be_rechecked_without_reapplying_GPOs() {
        var wave=new Wave{Id=LabFixture.Id(),Name="Pilot",Members=[new(){GroupId=f.SqlClients.Id,ComputerIds=[f.ClientNo.Id]}]};
        var p=f.Plan with {Waves=[wave]}; var repo=new PlanRepository(root);
        var saved=await repo.Save(null,p,f.Snapshot,PlanRules.Assess(p,f.Snapshot),"op");
        await repo.AppendDeployment(new(DateTimeOffset.UtcNow,"op",saved.Id,1,"","apply",true,"{}"){PendingDcs=["dc.test"]});
        var runner=new Runner{Reply=s=>s.Contains("@{replication=(Wait-StudioReplication") ? (true,"{\"replication\":{\"dc.test\":\"ok\"}}") : s.Contains("external=$external") ? (true,ScriptReplies.CleanGpos(Compiler.Compile(saved))) : (false,"simulated mutation stop")};
        var d=new Deployment(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Deployment:Enabled"]="true"}).Build(),repo,runner,NullLogger<Deployment>.Instance);
        await d.ApplyWave(new(saved.Id,1,wave.Id,"wave-apply","approved"),"op");
        Assert.Contains(runner.Scripts,s=>s.Contains("Add-ADGroupMember"));
        Assert.Contains(await repo.ListDeployments(saved.Id),r=>r.Action=="replication-check" && r.Succeeded);
    }
    [Fact]
    public void Management_access_and_custom_base_match_compiled_authorization() {
        var s = f.With(f.SqlClients with { MemberIds = [f.App1.Id] });
        Assert.Equal("encrypted", f.Outcome(f.Admin1.Id, f.Db1.Id, snapshot: s));
        Assert.Equal("blocked", f.Outcome(f.Fs1.Id, f.App1.Id, f.Plan with { BaseGroupId = f.SqlClients.Id }));
    }
    [Fact]
    public void Unilateral_management_denial_is_indeterminate_because_management_SAs_are_bidirectional() {
        var s=f.With(f.SqlClients with {MemberIds=[f.App1.Id]});
        var p=f.Plan with {BaseGroupId=f.SqlClients.Id};
        Assert.Equal("indeterminate",f.Outcome(f.Fs1.Id,f.Admin1.Id,p,s));
        Assert.Equal("indeterminate",f.Outcome(f.Admin1.Id,f.App1.Id,p,s));
        Assert.Equal("blocked",f.Outcome(f.Fs1.Id,f.App1.Id,p,s));
        Assert.Equal("encrypted",f.Outcome(f.Admin1.Id,f.Db1.Id,p,s));
    }
    [Fact]
    public void Quarantine_wins_over_no_policy_and_outside_ou_gets_no_gpo() {
        var s = f.With(f.Exclusion with { MemberIds = [f.Legacy.Id] });
        var q = Compiler.Compile(f.Saved(snapshot:s)).Gpos.Single(g => g.Profile == "quarantine");
        Assert.DoesNotContain(f.NoPolicy.Sid!, q.DenyGroupSids);
        var ou = new DirectoryObject { Id = LabFixture.Id(), Kind = "ou", Name = "Pilot", Dn = "OU=Pilot,DC=lab,DC=ipsec,DC=test" };
        s = f.Snapshot with { Objects = [..f.Snapshot.Objects, ou] };
        var p = f.Plan with { Settings = f.Plan.Settings with { LinkTarget = ou.Dn } };
        Assert.All(PlanRules.Assess(p,s).Hosts.Where(h => h.Profile != "dc"), h => Assert.Equal("none", h.Profile));
        Assert.Equal("clear", f.Outcome(f.App1.Id, f.Db1.Id,p,s));
        var wave=new Wave{Id=LabFixture.Id(),Name="Outside OU",Members=[new(){GroupId=f.SqlClients.Id,ComputerIds=[f.ClientNo.Id]}]};
        Assert.Throws<ValidationException>(()=>PlanRules.Assess(p with {Waves=[wave]},s));
    }
    [Fact]
    public void Blocked_inheritance_and_unknown_OU_evidence_do_not_predict_an_applied_policy() {
        var blocked=f.With(f.PilotOu with {BlocksInheritance=true});
        Assert.Equal("clear",f.Outcome(f.App1.Id,f.Db1.Id,f.Plan,blocked));
        var local=f.Plan with {Settings=f.Plan.Settings with {LinkTarget=f.PilotOu.Dn}};
        Assert.Equal("encrypted",f.Outcome(f.App1.Id,f.Db1.Id,local,blocked)); // Blocking inheritance does not block a link on that OU itself.
        var unknown=f.With(f.PilotOu with {BlocksInheritance=null});
        Assert.Equal("indeterminate",f.Outcome(f.App1.Id,f.Db1.Id,f.Plan,unknown));
        var missing=f.Snapshot with {Objects=f.Snapshot.Objects.Where(o=>o.Id!=f.PilotOu.Id).ToArray()};
        Assert.Equal("indeterminate",f.Outcome(f.App1.Id,f.Db1.Id,f.Plan,missing));
        var w=new Wave{Id=LabFixture.Id(),Name="Unknown",Members=[new(){GroupId=f.SqlClients.Id,ComputerIds=[f.ClientNo.Id]}]};
        Assert.Contains(PlanRules.Assess(f.Plan with {Waves=[w]},unknown).Hosts,h=>!h.ScopeKnown);
    }
    [Fact]
    public async Task Apply_runner_exception_keeps_attempt_and_recovery_artifact() {
        var repo=new PlanRepository(root);var saved=await repo.Save(null,f.Plan,f.Snapshot,PlanRules.Assess(f.Plan,f.Snapshot),"op");var c=Compiler.Compile(saved);
        var runner=new Runner{Reply=s=>s.Contains("external=$external") ? ScriptReplies.Default(s) : throw new IOException("simulated lost process reply")};
        var d=new Deployment(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Deployment:Enabled"]="true"}).Build(),repo,runner,NullLogger<Deployment>.Instance);
        var result=await d.Execute(new(saved.Id,1,c.Sha256,"apply","approved"){AcknowledgeScope=true},"op");
        Assert.False(result.Succeeded);Assert.Contains("simulated lost process reply",result.Output);
        Assert.Equal(2,(await repo.ListDeployments(saved.Id)).Count(r=>r.Action=="apply"));
        Assert.Equal(c.ApplyScript,(await repo.AppliedCompilation(saved.Id,1))!.ApplyScript);
    }
    [Fact]
    public async Task Legacy_scope_metadata_blocks_new_waves_but_not_GPO_recovery() {
        var s=f.With(f.PilotOu with {BlocksInheritance=null});
        var w=new Wave{Id=LabFixture.Id(),Name="Legacy",Members=[new(){GroupId=f.SqlClients.Id,ComputerIds=[f.ClientNo.Id]}]};
        var p=f.Plan with {Waves=[w]};var repo=new PlanRepository(root);var saved=await repo.Save(null,p,s,PlanRules.Assess(p,s),"op");
        var c=Compiler.Compile(saved);await repo.AppendDeployment(new(DateTimeOffset.UtcNow,"op",saved.Id,1,c.Sha256,"apply",true,"{}"));
        var runner=new Runner();var d=new Deployment(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Deployment:Enabled"]="true"}).Build(),repo,runner,NullLogger<Deployment>.Instance);
        await Assert.ThrowsAsync<ConflictException>(()=>d.ApplyWave(new(saved.Id,1,w.Id,"wave-apply","approved"),"op"));
        Assert.Empty(runner.Scripts);
        await repo.Save(saved.Id,p with {Name="New edit",ExpectedVersion=1},s,PlanRules.Assess(p,s),"op");
        var result=await d.Execute(new(saved.Id,1,c.Sha256,"rollback"),"op");
        Assert.True(result.Succeeded);
    }
    [Fact]
    public void Empty_waves_and_shared_addresses_do_not_claim_an_operational_result() {
        var p=f.Plan with {Waves=[new(){Id=LabFixture.Id(),Name="Empty",Members=[]}]};
        Assert.Throws<ValidationException>(()=>PlanRules.Assess(p,f.Snapshot));
        var s=f.With(f.ClientNo with {Ip=f.App1.Ip});
        Assert.Equal("indeterminate",f.Outcome(f.App1.Id,f.ClientNo.Id,f.Plan,s));
    }
    [Fact]
    public void Matrix_is_bounded_even_if_the_domain_inventory_is_large() {
        var extra = Enumerable.Range(0,2000).Select(i => f.App1 with { Id=LabFixture.Id(), Sid=LabFixture.DomainSid+"-"+(50000+i), Name="host"+i, Dn=$"CN=host{i},{f.PilotOu.Dn}", Ip=$"10.74.{2+i/250}.{1+i%250}" }).ToArray();
        var baseGroup=f.DomainComputers with {MemberIds=[..f.DomainComputers.MemberIds,..extra.Select(h=>h.Id)]};
        var initial=f.With(baseGroup); var s = initial with { Objects = [..initial.Objects, ..extra] };
        var p=f.Plan with {Settings=f.Plan.Settings with {SecureSubnets=["10.74.0.0/16"]}};
        var assessment=PlanRules.Assess(p,s);
        var chosen=Simulator.Matrix(p,s,assessment,[extra[0].Id,extra[^1].Id]);
        Assert.Equal(2,chosen.Length); Assert.All(chosen,p=>Assert.Equal("authenticated",p.Outcome));
        Assert.InRange(Simulator.Matrix(p,s,PlanRules.Assess(p,s)).Length,1,30*29);
    }
    [Fact]
    public async Task Pilot_probe_does_not_scan_unselected_domain_computers() {
        var p=f.Plan with {BaseGroupId=f.SqlClients.Id,Restricted=[],ExternalHosts=[],BoundaryGroupId=null,NoPolicyGroupId=null,ExclusionGroupId=null,Settings=f.Plan.Settings with {ManagementGroupId=null}};
        var repo=new PlanRepository(root);var saved=await repo.Save(null,p,f.Snapshot,PlanRules.Assess(p,f.Snapshot),"op");
        var runner=new Runner{Reply=_=>(true,"[]")};var d=new Deployment(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Deployment:Enabled"]="true"}).Build(),repo,runner,NullLogger<Deployment>.Instance);
        await d.Probe(new(saved.Id,1,"probe"),"op");
        var script=Assert.Single(runner.Scripts);
        Assert.Contains("\"Name\":\"app1\"",script);Assert.DoesNotContain("\"Name\":\"db1\"",script);Assert.DoesNotContain("\"Name\":\"client-no\"",script);
    }
    [Fact]
    public void DCs_are_denied_GPO_application_even_if_they_join_a_profile_group() {
        var s=f.With(f.SqlServers with {MemberIds=[f.Db1.Id,f.Dc1.Id]});
        var c=Compiler.Compile(f.Saved(snapshot:s));
        Assert.All(c.Gpos,g=>{Assert.Contains(LabFixture.DomainSid+"-516",g.DenyGroupSids);Assert.Contains(LabFixture.DomainSid+"-521",g.DenyGroupSids);});
    }
    [Fact]
    public void Wave_rollback_uses_an_ownership_journal_instead_of_removing_every_requested_member() {
        var wave = new Wave { Id = LabFixture.Id(), Name = "Pilot", Members = [new() { GroupId = f.SqlClients.Id, ComputerIds = [f.App1.Id] }] };
        var c = Compiler.CompileWave(f.Saved(f.Plan with { Waves = [wave] }), wave.Id);
        Assert.Contains("journal", c.ApplyScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CreateNew", c.ApplyScript);
        Assert.Contains("added", c.RollbackScript);
        Assert.Contains("pending", c.RollbackScript);
        Assert.DoesNotContain($"Remove-ADGroupMember -Identity '{f.SqlClients.Sid}' -Members '{f.App1.Sid}'", c.RollbackScript);
    }
}
