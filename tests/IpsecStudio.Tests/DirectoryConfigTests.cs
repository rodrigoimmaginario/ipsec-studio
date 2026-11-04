using IpsecStudio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IpsecStudio.Tests;

// With several domain controllers the Studio should not depend on one of them: an empty Directory:Server means the domain
// name itself, so the AD cmdlets use the DC locator (lab, CV4.DS5: dc1 deallocated, dc2 answering).
public class DirectoryConfigTests {
    static DirectoryReader Reader(string? server) => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["Directory:Server"] = server, ["Directory:ExpectedDomain"] = "lab.ipsec.test" }).Build(), null!, NullLogger<DirectoryReader>.Instance);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_server_falls_back_to_the_domain_name(string? server) => Assert.Equal("lab.ipsec.test", Reader(server).Server);

    [Fact]
    public async Task Domain_confirmation_is_local_and_required_before_collection() {
        var root=Path.Combine(Path.GetTempPath(),"ipsec-directory-"+LabFixture.Id());
        try {
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Storage:Root"]=root,["Directory:ExpectedDomain"]=""}).Build();
            var reader=new DirectoryReader(config,null!,NullLogger<DirectoryReader>.Instance);
            await Assert.ThrowsAsync<ValidationException>(()=>reader.Refresh(CancellationToken.None));
            await Assert.ThrowsAsync<ValidationException>(()=>reader.Configure("bad'; command"));
            await reader.Configure("example.test");
            Assert.Equal("example.test",reader.Domain); Assert.Equal("example.test",reader.Server);
            Assert.True(File.Exists(DirectoryReader.SettingsPath(config)));
            var restored=new ConfigurationBuilder().AddJsonFile(DirectoryReader.SettingsPath(config)).Build();
            Assert.Equal("example.test",restored["Directory:ExpectedDomain"]);
        } finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
    [Fact]
    public void An_explicit_domain_controller_is_still_honoured() => Assert.Equal("dc2.lab.ipsec.test", Reader("dc2.lab.ipsec.test").Server);
}
