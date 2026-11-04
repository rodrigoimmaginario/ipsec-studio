# Security model and limits

[Português](../pt-br/seguranca.md)

## Identity and authority

- Every action uses the **Windows identity of the process**, like GPMC. There is no Studio service account or password.
- The service listens on `127.0.0.1`. Windows Authentication (Negotiate) and SID comparison require the operator who started it, including for API reads. The browser may prompt for that Windows account.
- The page token, Host/origin validation and content policy protect the browser boundary; they do not replace Windows authentication. **Do not expose the port.** Use one instance on a controlled administration workstation, not a shared service.
- The package starts with `Deployment:Enabled=false`: reading/planning does not enable domain changes. Enable it deliberately in configuration and restart.
- Storage permissions allow the operator, SYSTEM and local administrators; those administrators remain trusted. Protect copies and backups too.

## What the Studio changes

- `IpsecStudio-*` GPOs carrying the plan identifier/revision in their description; links at the selected target. Rollback requires the revision marker.
- Declared new groups and computer memberships in waves, only on request with approval. No firewall rules or permanent agent are installed.
- On hosts over WinRM: a temporary SYSTEM task for GPO refresh, machine-ticket purge and SA cleanup; rule reads, probe connections and optional capture, deleted afterwards when the host remains reachable.
- Ticket purge and SA cleanup may affect existing services and sessions. The probe is not passive observation. Avoid concurrent interventions and keep independent console access.

## Script construction and recovery

Data is serialized as JSON before entering PowerShell, including group containers. Apply/rollback check the preview hash. New applies preserve the compiled artifact; editing and deployment revisions are distinct. Wave journals persist membership ownership; ambiguous states are not undone automatically.

Approval is an operator note, not a second-person approval system. Local records are operational evidence, not an administrator-tamper-proof log. See [recovery](recovery.md), including legacy installations without journals/artifacts.

## Meaning of evidence

| Evidence | What it supports | Limit |
|---|---|---|
| Plan and matrix | prediction from snapshot and modeled scope | not discovery of all flows or a guarantee of GPO application |
| GPO in AD | configuration created/linked and compared | not proof that every host/DC has received it |
| Effective policy | rules, filters, modes, authorization, crypto, source and status match intent | unreachable hosts or incomplete readings remain unverified |
| TCP + SA | connectivity and negotiated protection in the probe | failure to connect does not identify the cause; an SA may be reused |
| ESP capture | protected packets observed for the pair | ESP may use null encryption; distinguishing encryption from integrity needs the SA |

## Operational limits

- Domain Computers at the root is not a limited pilot: policy becomes eligible before waves. Start with a dedicated OU/group.
- Prediction assumes a fresh negotiation. SAs are bidirectional: a management-opened SA may allow reverse traffic even when a unilateral list would deny a fresh negotiation. That combination is indeterminate, cannot approve expansion, and is not fixed by widening permissions. Do not substitute IPsec for a firewall to promise per-connection blocking.
- The matrix uses up to 30 machines per query; inventory has no such limit. Complete probing remains expensive in large networks.
- Configurable addresses/subnets are IPv4; an `Any` rule is not equivalent to listing only IPv4 prefixes. Traffic outside configured scope is not covered by the isolation claim.
- Studio GPOs deny Apply Group Policy to Domain Controllers and Read-only Domain Controllers, even after accidental profile-group membership. DCs, exempt addresses, ICMP and boundary have deliberate exceptions. Quarantine is not EDR/firewall containment and does not block every external connection.
- A ticket newer than a group change is an **indication**, not proof of ticket groups or replication on the issuing DC. Effective policy and tests remain necessary.
- For its own non-enforced links, inventory reads OU inheritance blocking: a block below the link excludes the host; missing evidence makes prediction indeterminate. Old inventories need refreshing before new waves; GPO recovery remains available.
- Several AD sites, legacy IPsec policies, other GPO reach (inheritance/enforced links/additional filtering), SIDHistory and localized Windows need specific validation. The package has no Authenticode signature.
- Compiler/simulator/deployment changes are accepted only after a new AD regression. Local tests and C# coverage alone do not prove PowerShell cmdlet behavior.
