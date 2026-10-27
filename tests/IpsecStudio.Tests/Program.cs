using IpsecStudio;
using System.Text.Json;
static string Id() => Guid.NewGuid().ToString("D");
static void Assert(bool condition, string label) { if (!condition) throw new Exception(label); }
var passed = new List<string>();
void Check(string name, Action test) { test(); passed.Add(name); Console.WriteLine("PASS " + name); }
void Rejects(Func<object> action, string label) { try { action(); throw new Exception("Accepted invalid input: " + label); } catch (ValidationException) { } }

// Lab-shaped fixture: dc1, app1 (access), db1 (restricted+encryption), fs1 (restricted, authenticated only), client-no (base), admin1 (management), wsus (boundary), legacy (no policy), ext1 (outside AD).
const string DomainSid = "S-1-5-21-1111111111-2222222222-3333333333";
int rid = 1000; string Sid() => $"{DomainSid}-{++rid}";
DirectoryObject Computer(string name, string ip, string kind = "server") => new() { Id = Id(), Name = name, Kind = kind, Ip = ip, Sid = Sid(), Dn = $"CN={name},OU=IpsecProof,DC=lab,DC=ipsec,DC=test" };
DirectoryObject Group(string name, params DirectoryObject[] members) => new() { Id = Id(), Name = name, Kind = "group", Sid = Sid(), MemberIds = members.Select(m => m.Id).ToArray() };
var dc1 = Computer("dc1", "10.74.1.10", "dc"); var app1 = Computer("app1", "10.74.1.20"); var db1 = Computer("db1", "10.74.1.30"); var fs1 = Computer("fs1", "10.74.1.40");
var clientNo = Computer("client-no", "10.74.1.50"); var admin1 = Computer("admin1", "10.74.1.70"); var wsus = Computer("wsus", "10.74.1.80"); var legacy = Computer("legacy", "10.74.1.90");
var domainComputers = new DirectoryObject { Id = Id(), Name = "Domain Computers", Kind = "group", Sid = DomainSid + "-515", MemberIds = [app1.Id, db1.Id, fs1.Id, clientNo.Id, admin1.Id, wsus.Id, legacy.Id] };
var adminClients = Group("IPSEC-Admin-Clients", admin1);
var sqlServers = Group("IPSEC-SQL-Servers", db1); var sqlClients = Group("IPSEC-SQL-Clients", app1, admin1);
var fileServers = Group("IPSEC-File-Servers", fs1); var fileClients = new DirectoryObject { Id = Id(), Name = "IPSEC-File-Clients", Kind = "group", Sid = Sid(), MemberIds = [app1.Id, adminClients.Id] };
var boundary = Group("IPSEC-Boundary", wsus); var noPolicy = Group("IPSEC-NoPolicy", legacy); var exclusion = Group("IPSEC-Exclusion");
var snapshot = new InventorySnapshot { Id = Id(), CapturedAt = DateTimeOffset.UtcNow, Domain = "lab.ipsec.test", DomainSid = DomainSid, DomainDn = "DC=lab,DC=ipsec,DC=test", Identity = "test",
    Objects = [dc1, app1, db1, fs1, clientNo, admin1, wsus, legacy, domainComputers, adminClients, sqlServers, sqlClients, fileServers, fileClients, boundary, noPolicy, exclusion] };
var ext1 = new ExternalHost { Id = Id(), Name = "ext1", Address = "10.74.1.60" };
var sql = new RestrictedServers { Id = Id(), Name = "Banco de dados", ProtectedGroupId = sqlServers.Id, AccessGroupId = sqlClients.Id, Encryption = true };
var files = new RestrictedServers { Id = Id(), Name = "Arquivos", ProtectedGroupId = fileServers.Id, AccessGroupId = fileClients.Id, Encryption = false };
var plan = new PlanInput { Name = "Lab", InventoryId = snapshot.Id, Restricted = [sql, files], BoundaryGroupId = boundary.Id, NoPolicyGroupId = noPolicy.Id, ExclusionGroupId = exclusion.Id, ExternalHosts = [ext1],
    Settings = new() { ManagementGroupId = adminClients.Id, SecureSubnets = ["10.74.1.0/24"] } };
string Outcome(PlanInput p, InventorySnapshot s, string from, string to) => Simulator.Evaluate(p, s, PlanRules.Assess(p, s), from, to).Outcome;

Check("Each computer resolves to exactly one profile; base is Domain Computers minus other profiles", () => {
    var a = PlanRules.Assess(plan, snapshot);
    string P(DirectoryObject c) => a.Hosts.Single(h => h.ComputerId == c.Id).Profile;
    Assert(P(dc1) == "dc" && P(app1) == "base" && P(clientNo) == "base" && P(db1) == "restricted" && P(fs1) == "restricted" && P(admin1) == "management" && P(wsus) == "boundary" && P(legacy) == "noPolicy", "profiles");
    Assert(a.DomainControllerAddresses.SequenceEqual(["10.74.1.10"]) && a.Hosts.All(h => h.Conflicts.Length == 0), "dc list / no conflicts");
    Assert(a.Profiles.Single(p => p.Key == "restricted:" + sql.Id).ComputerIds.SequenceEqual([db1.Id]), "profile summary");
});
Check("Conflicting memberships are reported and block compilation", () => {
    var both = fileServers with { MemberIds = [fs1.Id, db1.Id] };
    var s = snapshot with { Objects = snapshot.Objects.Select(o => o.Id == fileServers.Id ? both : o).ToArray() };
    var a = PlanRules.Assess(plan, s);
    Assert(a.Hosts.Single(h => h.ComputerId == db1.Id).Conflicts.Length == 1, "conflict");
    Rejects(() => Compiler.Compile(new SavedPlan(Id(), 1, "x", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "t", plan, s, a)), "compile with conflict");
});
Check("Invalid references, SIDs, addresses and crypto are rejected", () => {
    Rejects(() => PlanRules.Assess(plan with { BoundaryGroupId = Id() }, snapshot), "unknown group");
    Rejects(() => PlanRules.Assess(plan with { Exemptions = [new() { Id = Id(), Name = "x", Address = "10.74.1", Reason = "r", Owner = "o" }] }, snapshot), "bad ip");
    Rejects(() => PlanRules.Assess(plan with { Settings = plan.Settings with { SecureSubnets = ["10.74.0.0/4"] } }, snapshot), "bad cidr");
    Rejects(() => PlanRules.Assess(plan with { Settings = plan.Settings with { Crypto = new() { MainModeEncryption = "DES3" } } }, snapshot), "legacy cipher");
    Rejects(() => PlanRules.Assess(plan, snapshot with { Objects = snapshot.Objects.Select(o => o.Id == sqlServers.Id ? o with { Sid = "S-1-5-32-544" } : o).ToArray() }), "non-domain sid");
    Rejects(() => PlanRules.Assess(plan, snapshot with { Objects = snapshot.Objects.Select(o => o.Id == sqlServers.Id ? o with { Name = "x';Remove-GPO" } : o).ToArray() }), "group name injection");
    Rejects(() => PlanRules.Assess(plan with { BaseMode = "strict" }, snapshot), "mode");
});
Check("Simulation: access group reaches encrypted server, others are blocked", () => {
    Assert(Outcome(plan, snapshot, app1.Id, db1.Id) == "encrypted", "app1→db1");
    Assert(Outcome(plan, snapshot, admin1.Id, db1.Id) == "encrypted", "admin1→db1");
    Assert(Outcome(plan, snapshot, app1.Id, fs1.Id) == "authenticated", "app1→fs1 (nested access)");
    Assert(Outcome(plan, snapshot, clientNo.Id, db1.Id) == "blocked", "client-no→db1");
    Assert(Outcome(plan, snapshot, ext1.Id, db1.Id) == "blocked", "ext1→db1");
    Assert(Outcome(plan, snapshot, db1.Id, fs1.Id) == "blocked", "db1→fs1 (not in each other's access)");
    Assert(Outcome(plan, snapshot, db1.Id, app1.Id) == "encrypted" && Outcome(plan, snapshot, db1.Id, clientNo.Id) == "encrypted", "db1→base: responder authorizes any domain computer (lab-observed semantics)");
});
Check("Simulation: base, boundary, no-policy, external and DC semantics", () => {
    Assert(Outcome(plan, snapshot, clientNo.Id, app1.Id) == "authenticated", "base↔base");
    Assert(Outcome(plan, snapshot, ext1.Id, app1.Id) == "blocked", "ext1→base");
    Assert(Outcome(plan, snapshot, app1.Id, ext1.Id) == "clear", "base→ext1 fallback");
    Assert(Outcome(plan, snapshot, db1.Id, ext1.Id) == "blocked", "restricted→ext1 no fallback");
    Assert(Outcome(plan, snapshot, ext1.Id, wsus.Id) == "clear", "ext1→boundary");
    Assert(Outcome(plan, snapshot, app1.Id, wsus.Id) == "authenticated", "base→boundary negotiates");
    Assert(Outcome(plan, snapshot, legacy.Id, app1.Id) == "blocked" && Outcome(plan, snapshot, legacy.Id, wsus.Id) == "clear", "noPolicy host");
    Assert(Outcome(plan, snapshot, app1.Id, dc1.Id) == "clear" && Outcome(plan, snapshot, ext1.Id, dc1.Id) == "clear", "dc exempt");
    Assert(Outcome(plan with { BaseMode = "require" }, snapshot, app1.Id, ext1.Id) == "blocked", "strict base");
    Assert(Outcome(plan with { BaseMode = "request" }, snapshot, ext1.Id, app1.Id) == "clear", "request base accepts clear");
});
Check("Simulation: exempt address wins over profile; secure subnet bounds negotiation", () => {
    var exempt = plan with { Exemptions = [new() { Id = Id(), Name = "Impressora", Address = "10.74.1.60", Reason = "legado", Owner = "rede" }] };
    Assert(Outcome(exempt, snapshot, ext1.Id, db1.Id) == "clear" && Outcome(exempt, snapshot, db1.Id, ext1.Id) == "clear", "exempt ip");
    var far = plan with { ExternalHosts = [ext1 with { Address = "10.74.2.60" }] };
    Assert(Outcome(far, snapshot, far.ExternalHosts[0].Id, app1.Id) == "clear", "outside secure subnets");
    Assert(PlanRules.Assess(exempt with { Exemptions = [exempt.Exemptions[0] with { Address = "10.74.1.30" }] }, snapshot).Hosts.Single(h => h.ComputerId == db1.Id).Conflicts.Length == 1, "exempting a protected server is flagged");
});
Check("Simulation: quarantine denies everywhere except DCs, exemptions, boundary and management", () => {
    var q = exclusion with { MemberIds = [clientNo.Id] };
    var s = snapshot with { Objects = snapshot.Objects.Select(o => o.Id == exclusion.Id ? q : o).ToArray() };
    Assert(PlanRules.Assess(plan, s).Hosts.Single(h => h.ComputerId == clientNo.Id).Profile == "quarantine", "quarantine is the primary profile");
    Assert(Outcome(plan, s, clientNo.Id, app1.Id) == "blocked" && Outcome(plan, s, clientNo.Id, db1.Id) == "blocked", "quarantined cannot initiate to trusted hosts");
    Assert(Outcome(plan, s, app1.Id, clientNo.Id) == "blocked" && Outcome(plan, s, ext1.Id, clientNo.Id) == "blocked", "nobody but management reaches the quarantined machine");
    Assert(Outcome(plan, s, clientNo.Id, admin1.Id) == "authenticated" && Outcome(plan, s, admin1.Id, clientNo.Id) == "authenticated", "management ↔ quarantined (remediation)");
    Assert(Outcome(plan, s, clientNo.Id, dc1.Id) == "clear" && Outcome(plan, s, clientNo.Id, wsus.Id) == "authenticated", "dc and boundary");
    var noMgmt = plan with { Settings = plan.Settings with { ManagementGroupId = null } };
    Assert(Outcome(noMgmt, s, clientNo.Id, admin1.Id) == "blocked" && Outcome(noMgmt, s, admin1.Id, clientNo.Id) == "blocked", "without management group nobody talks to the quarantined host");
    var cq = Compiler.Compile(Saved(plan, s)); var qg = cq.Gpos.Single(g => g.Profile == "quarantine");
    Assert(qg.ApplyGroupSid == exclusion.Sid && qg.Inbound == "Require" && qg.AuthorizationSddl == $"O:LSD:(A;;CC;;;{adminClients.Sid})" && cq.Gpos.Where(g => g.Profile != "quarantine").All(g => g.DenyGroupSids.Contains(exclusion.Sid!)), "quarantine GPO wins over every other profile");
    Assert(Compiler.Compile(Saved(noMgmt, s)).Gpos.Single(g => g.Profile == "quarantine").AuthorizationSddl == "O:LSD:", "empty DACL authorizes nobody");
    Assert(Simulator.Matrix(plan, s, PlanRules.Assess(plan, s)).Length == 8 * 7, "full matrix excludes DC, includes external");
});
SavedPlan Saved(PlanInput p, InventorySnapshot s, int version = 1) => new(Id(), version, p.Name, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "t", p, s, PlanRules.Assess(p, s));
Check("Compiler: one GPO per profile, deny-apply isolates base, authorization lists carry allow and deny SIDs", () => {
    var c = Compiler.Compile(Saved(plan, snapshot));
    Assert(c.Gpos.Select(g => g.Profile).SequenceEqual(["base", "restricted", "restricted", "boundary", "management", "quarantine"]), "gpo set");
    var b = c.Gpos[0];
    Assert(b.ApplyGroupSid == DomainSid + "-515" && b.DenyGroupSids.ToHashSet().SetEquals([sqlServers.Sid!, fileServers.Sid!, boundary.Sid!, adminClients.Sid!, noPolicy.Sid!, exclusion.Sid!]), "base deny set");
    Assert(b.AuthorizationSddl == $"O:LSD:(D;;CC;;;{exclusion.Sid})(A;;CC;;;{DomainSid}-515)" && b.Inbound == "Require" && b.Outbound == "Request" && b.RequireAuthorization, "base sddl/mode");
    var s = c.Gpos.Single(g => g.ApplyGroup == "IPSEC-SQL-Servers");
    Assert(s.Encryption && s.Inbound == "Require" && s.Outbound == "Require" && s.AuthorizationSddl == $"O:LSD:(D;;CC;;;{exclusion.Sid})(A;;CC;;;{sqlServers.Sid})(A;;CC;;;{sqlClients.Sid})(A;;CC;;;{adminClients.Sid})" && s.DenyGroupSids.SequenceEqual([noPolicy.Sid!, exclusion.Sid!]), "restricted sddl");
    var bd = c.Gpos.Single(g => g.Profile == "boundary"); Assert(bd.Inbound == "Request" && !bd.RequireAuthorization && bd.AuthorizationSddl is null, "boundary");
    var m = c.Gpos.Single(g => g.Profile == "management"); Assert(m.AuthorizationSddl == $"O:LSD:(A;;CC;;;{DomainSid}-515)", "management has no deny");
    Assert(c.ExemptAddresses.SequenceEqual(["10.74.1.10"]) && c.ApplyScript.Contains("\"exemptAddresses\":[\"10.74.1.10\"]") && c.ApplyScript.Contains("\"secureSubnets\":[\"10.74.1.0/24\"]"), "exemptions and subnets in spec");
});
Check("Compiler: deterministic hash, marker ties scripts to plan revision, rollback only touches marked GPOs", () => {
    var saved = Saved(plan, snapshot, 3);
    var c1 = Compiler.Compile(saved); var c2 = Compiler.Compile(saved);
    Assert(c1.Sha256 == c2.Sha256 && c1.Sha256.Length == 64, "deterministic");
    Assert(Compiler.Compile(saved with { Version = 4 }).Sha256 != c1.Sha256, "version changes hash");
    Assert(c1.Marker == $"IpsecStudio plan {saved.Id} v3" && c1.ApplyScript.Contains(c1.Marker) && c1.RollbackScript.Contains("Description -ne $spec.marker"), "marker");
    Assert(c1.ApplyScript.Contains("-RemoteMachineTransportAuthorizationList") && c1.ApplyScript.Contains("Deny-Apply") && c1.ApplyScript.Contains("-InboundSecurity None -OutboundSecurity None") && !c1.ApplyScript.Contains("New-NetFirewallRule"), "native IPsec only, no firewall rules");
    Assert(c1.RollbackScript.Contains("Remove-GPO -Guid") && !c1.RollbackScript.Contains("New-GPO"), "rollback");
    var lines = c1.ApplyScript.Split('\n'); var specLine = lines.Single(l => l.StartsWith("{\"domain\""));
    Assert(lines.Count(l => l.Contains("IPSEC-SQL-Servers")) == 1 && specLine.Contains("IPSEC-SQL-Servers"), "group names only inside the JSON literal");
});
Check("Compiler: optional profiles collapse cleanly", () => {
    var minimal = new PlanInput { Name = "Min", InventoryId = snapshot.Id };
    var c = Compiler.Compile(Saved(minimal, snapshot));
    Assert(c.Gpos.Length == 1 && c.Gpos[0].DenyGroupSids.Length == 0 && c.Gpos[0].AuthorizationSddl == $"O:LSD:(A;;CC;;;{DomainSid}-515)", "base only");
    Assert(PlanRules.Assess(minimal, snapshot).Hosts.Count(h => h.Profile == "base") == 7, "all computers in base");
});
var folder = Path.Combine(Path.GetTempPath(), "ipsec-tests-" + Id());
try {
    Check("Repository: schema 2 round trip, deployment log, legacy documents ignored", () => {
        var repo = new PlanRepository(folder);
        var saved = repo.Save(null, plan, snapshot, PlanRules.Assess(plan, snapshot), "tester").Result;
        var again = new PlanRepository(folder).Get(saved.Id).Result!;
        Assert(again.SchemaVersion == 2 && again.Input.Restricted.Length == 2 && again.Inventory.DomainSid == DomainSid && again.Input.ExpectedVersion is null, "round trip");
        try { repo.Save(saved.Id, plan with { ExpectedVersion = 0 }, snapshot, again.Assessment, "tester").Wait(); throw new Exception("stale accepted"); } catch (AggregateException e) when (e.InnerException is ConflictException) { }
        var v2 = repo.Save(saved.Id, plan with { ExpectedVersion = 1, Name = "Lab v2" }, snapshot, again.Assessment, "tester").Result;
        Assert(v2.Version == 2 && File.Exists(Path.Combine(folder, saved.Id, "000001.json")), "immutable versions");
        repo.AppendDeployment(new(DateTimeOffset.UtcNow, "tester", saved.Id, 2, "abc", "apply", true, "{}")).Wait();
        repo.AppendDeployment(new(DateTimeOffset.UtcNow.AddSeconds(1), "tester", saved.Id, 2, "", "verify", false, "x")).Wait();
        var log = repo.ListDeployments(saved.Id).Result; Assert(log.Length == 2 && log[0].Action == "apply" && !log[1].Succeeded, "deployment log");
        var legacyDir = Path.Combine(folder, Id()); Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir, "000001.json"), "{\"id\":\"x\",\"input\":{\"segments\":[]}}");
        Assert(repo.List().Result.Count == 1, "legacy ignored");
        var hash = Compiler.Compile(v2).Sha256;
        Assert(JsonSerializer.Deserialize<SavedPlan>(JsonSerializer.Serialize(v2, Json.Settings), Json.Settings)!.Input.Settings.Crypto.KeyExchange == "DH14", "settings survive json");
        Assert(hash == Compiler.Compile(new PlanRepository(folder).Get(saved.Id).Result!).Sha256, "hash reproducible from disk");
    });
} finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
Console.WriteLine(JsonSerializer.Serialize(new { passed = passed.Count, tests = passed }, new JsonSerializerOptions { WriteIndented = true }));
