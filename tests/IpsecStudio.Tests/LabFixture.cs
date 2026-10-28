using IpsecStudio;

namespace IpsecStudio.Tests;

// Lab-shaped fixture: dc1, app1 (access), db1 (restricted + encryption), fs1 (restricted, authenticated only),
// client-no (base), admin1 (management), wsus (boundary), legacy (no policy), ext1 (outside AD).
public sealed class LabFixture {
    public const string DomainSid = "S-1-5-21-1111111111-2222222222-3333333333";
    int rid = 1000;
    public static string Id() => Guid.NewGuid().ToString("D");
    string Sid() => $"{DomainSid}-{++rid}";
    DirectoryObject Computer(string name, string ip, string kind = "server") => new() { Id = Id(), Name = name, Kind = kind, Ip = ip, Sid = Sid(), Dn = $"CN={name},OU=IpsecProof,DC=lab,DC=ipsec,DC=test" };
    DirectoryObject Group(string name, params DirectoryObject[] members) => new() { Id = Id(), Name = name, Kind = "group", Sid = Sid(), MemberIds = members.Select(m => m.Id).ToArray() };

    public readonly DirectoryObject Dc1, App1, Db1, Fs1, ClientNo, Admin1, Wsus, Legacy;
    public readonly DirectoryObject DomainComputers, AdminClients, SqlServers, SqlClients, FileServers, FileClients, Boundary, NoPolicy, Exclusion;
    public readonly InventorySnapshot Snapshot;
    public readonly ExternalHost Ext1;
    public readonly RestrictedServers Sql, Files;
    public readonly PlanInput Plan;

    public LabFixture() {
        Dc1 = Computer("dc1", "10.74.1.10", "dc"); App1 = Computer("app1", "10.74.1.20"); Db1 = Computer("db1", "10.74.1.30"); Fs1 = Computer("fs1", "10.74.1.40");
        ClientNo = Computer("client-no", "10.74.1.50"); Admin1 = Computer("admin1", "10.74.1.70"); Wsus = Computer("wsus", "10.74.1.80"); Legacy = Computer("legacy", "10.74.1.90");
        DomainComputers = new() { Id = Id(), Name = "Domain Computers", Kind = "group", Sid = DomainSid + "-515", MemberIds = [App1.Id, Db1.Id, Fs1.Id, ClientNo.Id, Admin1.Id, Wsus.Id, Legacy.Id] };
        AdminClients = Group("IPSEC-Admin-Clients", Admin1);
        SqlServers = Group("IPSEC-SQL-Servers", Db1); SqlClients = Group("IPSEC-SQL-Clients", App1, Admin1);
        FileServers = Group("IPSEC-File-Servers", Fs1); FileClients = new() { Id = Id(), Name = "IPSEC-File-Clients", Kind = "group", Sid = Sid(), MemberIds = [App1.Id, AdminClients.Id] };
        Boundary = Group("IPSEC-Boundary", Wsus); NoPolicy = Group("IPSEC-NoPolicy", Legacy); Exclusion = Group("IPSEC-Exclusion");
        Snapshot = new() { Id = Id(), CapturedAt = DateTimeOffset.UtcNow, Domain = "lab.ipsec.test", DomainSid = DomainSid, DomainDn = "DC=lab,DC=ipsec,DC=test", Identity = "test",
            Objects = [Dc1, App1, Db1, Fs1, ClientNo, Admin1, Wsus, Legacy, DomainComputers, AdminClients, SqlServers, SqlClients, FileServers, FileClients, Boundary, NoPolicy, Exclusion] };
        Ext1 = new() { Id = Id(), Name = "ext1", Address = "10.74.1.60" };
        Sql = new() { Id = Id(), Name = "Banco de dados", ProtectedGroupId = SqlServers.Id, AccessGroupId = SqlClients.Id, Encryption = true };
        Files = new() { Id = Id(), Name = "Arquivos", ProtectedGroupId = FileServers.Id, AccessGroupId = FileClients.Id, Encryption = false };
        Plan = new() { Name = "Lab", InventoryId = Snapshot.Id, Restricted = [Sql, Files], BoundaryGroupId = Boundary.Id, NoPolicyGroupId = NoPolicy.Id, ExclusionGroupId = Exclusion.Id, ExternalHosts = [Ext1],
            Settings = new() { ManagementGroupId = AdminClients.Id, SecureSubnets = ["10.74.1.0/24"] } };
    }
    public string Outcome(string from, string to, PlanInput? plan = null, InventorySnapshot? snapshot = null) {
        var p = plan ?? Plan; var s = snapshot ?? Snapshot;
        return Simulator.Evaluate(p, s, PlanRules.Assess(p, s), from, to).Outcome;
    }
    public InventorySnapshot With(DirectoryObject replacement) => Snapshot with { Objects = Snapshot.Objects.Select(o => o.Id == replacement.Id ? replacement : o).ToArray() };
    public SavedPlan Saved(PlanInput? plan = null, InventorySnapshot? snapshot = null, int version = 1) {
        var p = plan ?? Plan; var s = snapshot ?? Snapshot;
        return new(Id(), version, p.Name, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "t", p, s, PlanRules.Assess(p, s));
    }
}
