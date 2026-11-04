using System.Security.Claims;
using System.Security.AccessControl;
using System.Security.Principal;
namespace IpsecStudio;

public static class LocalOperator {
    public static bool Allowed(ClaimsPrincipal user, string ownerSid) => user.Identity?.IsAuthenticated == true && user.FindFirst(ClaimTypes.PrimarySid)?.Value == ownerSid;
    // Plans and ownership journals must not inherit write permission for unrelated local users.
    public static void ProtectStorage(string root) {
        if (!OperatingSystem.IsWindows()) return;
        var owner=WindowsIdentity.GetCurrent().User ?? throw new UnauthorizedAccessException("Windows user SID required.");
        var security=new DirectorySecurity(); security.SetOwner(owner); security.SetAccessRuleProtection(true,false);
        foreach(var sid in new[]{owner,new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null)})
            security.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
        new DirectoryInfo(root).SetAccessControl(security);
    }
}
