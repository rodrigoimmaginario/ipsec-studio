using System.Security.Claims;
using IpsecStudio;
using Xunit;
namespace IpsecStudio.Tests;
public class LocalOperatorTests {
    [Fact]
    public void Only_authenticated_process_owner_can_use_the_local_service() {
        const string owner="S-1-5-21-1-2-3-1001";
        ClaimsPrincipal Principal(string sid,bool authenticated=true) => new(new ClaimsIdentity([new Claim(ClaimTypes.PrimarySid,sid)],authenticated ? "Negotiate" : null));
        Assert.False(LocalOperator.Allowed(new ClaimsPrincipal(),owner));
        Assert.False(LocalOperator.Allowed(Principal(owner,false),owner));
        Assert.False(LocalOperator.Allowed(Principal("S-1-5-21-1-2-3-1002"),owner));
        Assert.True(LocalOperator.Allowed(Principal(owner),owner));
    }
}
