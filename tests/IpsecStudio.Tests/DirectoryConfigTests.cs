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
    public void An_explicit_domain_controller_is_still_honoured() => Assert.Equal("dc2.lab.ipsec.test", Reader("dc2.lab.ipsec.test").Server);
}
