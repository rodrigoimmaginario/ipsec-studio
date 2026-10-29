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
        Assert.DoesNotMatch(@"(New-NetIPsecw+|Set-NetFirewallSetting) -PolicyStore $store", c.ApplyScript);
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
