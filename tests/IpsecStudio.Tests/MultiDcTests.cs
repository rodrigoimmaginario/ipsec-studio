using Microsoft.Extensions.Configuration;
using IpsecStudio;
using Xunit;

namespace IpsecStudio.Tests;

// Lab, CV4.DS5 (dc1 + dc2): New-GPO wrote on the PDC emulator while Get-Acl on the default AD: drive read another DC where the GPO
// had not replicated yet ("Cannot find path AD:\CN={...}"). Every generated script pins AD and GPO work to the PDC emulator.
public class MultiDcTests {
    readonly LabFixture f = new();
    const string Pin = "$pdc=$domain.PDCEmulator";
    static readonly string WaveId = LabFixture.Id();

    [Fact]
    public void Apply_and_rollback_pin_ad_and_gpo_cmdlets_and_the_acl_drive_to_the_pdc() {
        var c = Compiler.Compile(f.Saved());
        foreach (var s in new[] { c.ApplyScript, c.RollbackScript }) {
            Assert.Contains(Pin, s);
            Assert.Contains("$PSDefaultParameterValues['*-AD*:Server']=$pdc", s);
            Assert.Contains("$PSDefaultParameterValues['*-GP*:Server']=$pdc", s);
            Assert.Contains("-PSProvider ActiveDirectory -Root '//RootDSE/' -Server $pdc", s);
        }
        Assert.Contains("$path='IpsecPdc:\\CN='", c.ApplyScript);
        Assert.DoesNotContain("'AD:\\", c.ApplyScript);
        Assert.Contains("$gs=Open-NetGPO -PolicyStore $store -DomainController $pdc", c.ApplyScript);
        Assert.Contains("Save-NetGPO -GPOSession $gs", c.ApplyScript);
        Assert.DoesNotMatch(@"(New-NetIPsec\w+|Set-NetFirewallSetting) -PolicyStore \$store", c.ApplyScript);
        Assert.Matches(@"New-NetIPsecRule -GPOSession \$gs", c.ApplyScript);
    }

    [Fact]
    public void Group_and_wave_scripts_write_membership_on_the_pdc() {
        var plan = f.Plan with { Waves = [new Wave { Id = WaveId, Name = "Piloto", Members = [new WaveMember { GroupId = f.SqlClients.Id, ComputerIds = [f.ClientNo.Id] }] }],
                                 NewGroups = [new NewGroup { Id = LabFixture.Id(), Name = "IPSEC-Web-Servers", Description = "Servidores web" }] };
        var saved = f.Saved(plan);
        Assert.Contains("$PSDefaultParameterValues['*-AD*:Server']=$pdc", Compiler.CompileWave(saved, WaveId).ApplyScript);
        Assert.Contains("$PSDefaultParameterValues['*-AD*:Server']=$pdc", Compiler.CompileGroups(saved).ApplyScript);
    }
}

// Lab, CV4.DS5 (8 hosts): refresh removed every SA on the remote host, including the one carrying the Studio's own WinRM session;
// PowerShell then waited up to 4 minutes to reconnect per host and the 6-minute limit expired. The Studio's SAs are kept.
public class RefreshTests : IDisposable {
    readonly LabFixture f = new();
    readonly string folder = Path.Combine(Path.GetTempPath(), "ipsec-refresh-" + LabFixture.Id());
    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

    [Fact]
    public async Task Refresh_keeps_the_studio_security_associations() {
        var repo = new PlanRepository(folder);
        var saved = await repo.Save(null, f.Plan, f.Snapshot, PlanRules.Assess(f.Plan, f.Snapshot), "t");
        var runner = new Runner();
        var d = new Deployment(new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Deployment:Enabled"] = "true" }).Build(), repo, runner, Microsoft.Extensions.Logging.Abstractions.NullLogger<Deployment>.Instance);
        await d.HostOperation(new(saved.Id, 1, "refresh"), "op");
        var s = runner.Scripts.Single();
        Assert.Contains("$studioIps=ConvertFrom-Json", s);
        Assert.Contains("Where-Object {$_.RemoteEndpoint -notin $studioIps} | Remove-NetIPsecMainModeSA", s);
        Assert.Contains("Where-Object {$_.RemoteEndpoint -notin $studioIps} | Remove-NetIPsecQuickModeSA", s);
        Assert.True(runner.Timeouts.Single() >= TimeSpan.FromMinutes(6));
    }

    [Theory]
    [InlineData(1, 6)]
    [InlineData(5, 8)]
    [InlineData(8, 12)]
    public void Host_timeout_grows_with_the_number_of_hosts(int hosts, int minutes) => Assert.Equal(TimeSpan.FromMinutes(minutes), Deployment.HostsTimeout(hosts));

    sealed class Runner : IScriptRunner {
        public readonly List<string> Scripts = []; public readonly List<TimeSpan> Timeouts = [];
        public Task<(bool Ok, string Output)> Run(string script, TimeSpan timeout) { Scripts.Add(script); Timeouts.Add(timeout); return Task.FromResult((true, "[]")); }
    }
}
