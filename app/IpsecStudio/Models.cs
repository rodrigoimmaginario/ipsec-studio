namespace IpsecStudio;

public sealed record DirectoryObject {
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Dn { get; init; } = "";
    public string ParentDn { get; init; } = "";
    public string? Sid { get; init; }
    public string? Os { get; init; }
    public string? Ip { get; init; }
    public bool? Enabled { get; init; }
    public string[] MemberIds { get; init; } = [];
    public int UnresolvedMemberCount { get; init; }
    public string? PolicyPath { get; init; }
    public string? PolicyLinks { get; init; }
    public bool PolicyFileReadable { get; init; }
}
public sealed record InventorySnapshot {
    public string Id { get; init; } = "";
    public DateTimeOffset CapturedAt { get; init; }
    public string Domain { get; init; } = "";
    public string DomainSid { get; init; } = "";
    public string DomainDn { get; init; } = "";
    public string DomainPolicyLinks { get; init; } = "";
    public string Server { get; init; } = "";
    public string Identity { get; init; } = "";
    public DirectoryObject[] Objects { get; init; } = [];
    public string[] Warnings { get; init; } = [];
}

// ---- Plan (schema 2): Server and Domain Isolation expressed as five protection profiles. ----
// Cryptography and lifetimes are configuration with modern defaults, not model restrictions.
public sealed record CryptoSettings {
    public string MainModeEncryption { get; init; } = "AES256";
    public string MainModeHash { get; init; } = "SHA256";
    public string KeyExchange { get; init; } = "DH14";
    public string QuickModeEncryption { get; init; } = "AES256";
    public string QuickModeIntegrity { get; init; } = "SHA256";
    public int MainModeLifetimeMinutes { get; init; } = 180;
    public int QuickModeLifetimeMinutes { get; init; } = 60;
}
public sealed record PlanSettings {
    // Empty = any address. Otherwise IPv4 CIDRs that define the secure network (UVV "Rede Segura").
    public string[] SecureSubnets { get; init; } = [];
    public bool ExemptDomainControllers { get; init; } = true;
    public bool ExemptIcmp { get; init; } = true;
    // Outbound Require for restricted and quarantine. Off by default: in the lab it cut all traffic leaving the secure subnet.
    public bool RequireOutbound { get; init; }
    // "domain" or the DN of an OU where the GPOs are linked.
    public string LinkTarget { get; init; } = "domain";
    public CryptoSettings Crypto { get; init; } = new();
    // Optional group of management hosts that keep reaching quarantined computers.
    public string? ManagementGroupId { get; init; }
    // Container (DN) where "Prepare groups" creates new security groups. Null = CN=Users of the domain.
    public string? GroupContainer { get; init; }
}
// Security group the Studio creates on request, before it can be referenced by a profile (it needs a SID).
public sealed record NewGroup {
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
}
// A wave adds computers to profile groups. Waves are ordered; applying wave N assumes waves < N were applied.
public sealed record WaveMember {
    public string GroupId { get; init; } = "";
    public string[] ComputerIds { get; init; } = [];
}
public sealed record Wave {
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public WaveMember[] Members { get; init; } = [];
}
// Profile 1: protected servers reachable only by an access group; optional encryption; never falls back to clear.
public sealed record RestrictedServers {
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string ProtectedGroupId { get; init; } = "";
    public string? AccessGroupId { get; init; }
    public bool Encryption { get; init; }
    public bool Included { get; init; } = true;
}
// Profile 3 (addresses): peers that never use IPsec (printers, WSUS, firewalls, legacy). Declared, not discovered.
public sealed record ExemptAddress {
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Address { get; init; } = "";
    public string Reason { get; init; } = "";
    public string Owner { get; init; } = "";
    public bool Included { get; init; } = true;
}
// Hosts outside the directory used only for simulation/probing (e.g. an unmanaged workstation).
public sealed record ExternalHost {
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Address { get; init; } = "";
}
public sealed record PlanInput {
    public string Name { get; init; } = "";
    public string InventoryId { get; init; } = "";
    public int? ExpectedVersion { get; init; }
    public PlanSettings Settings { get; init; } = new();
    // Profile 2: the isolated domain. Default group is Domain Computers. Mode: secureRequest | request | require.
    public string? BaseGroupId { get; init; }
    public string BaseMode { get; init; } = "secureRequest";
    public RestrictedServers[] Restricted { get; init; } = [];
    public ExemptAddress[] Exemptions { get; init; } = [];
    // Profile 3 (computers): directory computers that receive no IPsec policy at all.
    public string? NoPolicyGroupId { get; init; }
    // Profile 4: accepts clear inbound from untrusted peers, negotiates IPsec when possible.
    public string? BoundaryGroupId { get; init; }
    // Profile 5: members cannot establish IPsec with any trusted computer (network exclusion group).
    public string? ExclusionGroupId { get; init; }
    public ExternalHost[] ExternalHosts { get; init; } = [];
    public NewGroup[] NewGroups { get; init; } = [];
    public Wave[] Waves { get; init; } = [];
}

// ---- Assessment: deterministic resolution of the plan against one inventory snapshot. ----
public sealed record HostProfile(string ComputerId, string Name, string? Ip, string Profile, string? RestrictedId, string[] Memberships, bool Excluded, string[] Conflicts);
public sealed record ProfileSummary(string Key, string Name, string[] ComputerIds, string[] Addresses, string[] Notes);
public sealed record PlanAssessment(HostProfile[] Hosts, ProfileSummary[] Profiles, string[] ExemptAddresses, string[] DomainControllerAddresses, string[] Warnings);

// ---- Simulation: what Windows IPsec is expected to do for one initiator → responder pair. ----
public sealed record Expectation(string Initiator, string Responder, string Outcome, string[] Reasons);

// ---- Compilation: the frozen artifact that will be executed. ----
public sealed record GpoSpec(string Name, string Profile, string ApplyGroup, string ApplyGroupSid, string[] DenyGroupSids, string Inbound, string Outbound, bool Encryption, bool RequireAuthorization, string? AuthorizationSddl, string[] ComputerIds);
public sealed record WaveCompilation(string WaveId, string Name, string[] ComputerIds, string ApplyScript, string RollbackScript, string Sha256);
public sealed record GroupsCompilation(string[] Names, string Container, string ApplyScript, string Sha256);
public sealed record Compilation(string PlanId, int PlanVersion, string SnapshotId, string Domain, string DomainSid, string Marker, GpoSpec[] Gpos, string[] ExemptAddresses, string ApplyScript, string RollbackScript, string Sha256) {
    // Settings written into every GPO; kept so the Studio can compare them with what AD holds later.
    public CryptoSettings Crypto { get; init; } = new();
    public bool ExemptIcmp { get; init; }
}

public sealed record SavedPlan(string Id, int Version, string Name, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string SavedBy, PlanInput Input, InventorySnapshot Inventory, PlanAssessment Assessment) {
    public int SchemaVersion => 2;
    public string ArtifactKind => "ipsec-isolation-plan";
}
public sealed class ValidationException(string message) : Exception(message);
public sealed class ConflictException(string message) : Exception(message);
