# Security model and limits

[Português](../pt-br/seguranca.md)

## Identity and authority

- The Studio has no users, passwords or tokens of its own. Every action uses the **Windows identity of the process** — the administrator who launched it. It is the same trust model as GPMC.
- The service listens on `127.0.0.1` only; requests with another `Host`, another origin or without the page token are refused. This protects against third-party pages in the same browser; it does not make the Studio a network service. **Do not publish the port.**
- An instance with `Deployment:Enabled=false` only reads AD and plans; useful for analysts without write permission.

## What the Studio writes

- GPOs named `IpsecStudio-*` whose description contains the plan identifier and version, linked at the chosen target. No existing GPO is modified; rollback removes only GPOs carrying the version marker.
- No firewall rules, no AD group changes, no agents on hosts.
- On hosts, over WinRM and only on request: `klist -li 0x3e7 purge`, `gpupdate`, SA removal, active rule reads, probe TCP connections.

## How the script is built

The compiler is constant code plus **a single JSON literal** with typed, validated values (domain-pattern SIDs, group names with a restricted pattern, IPv4/CIDR, closed suite catalog). No free text reaches PowerShell. The script's SHA-256 is displayed before execution and checked at apply/rollback time; a plan reopened at another version is refused.

## What the Studio proves and what it does not

| Evidence | Source | Limit |
|---|---|---|
| Saved plan | disk | intent, not policy |
| GPO created and linked | script output, GUIDs | does not mean applied on hosts |
| Effective policy | active rules with status, source GPO, authorization list | an unreachable host stays unverified |
| Traffic | TCP connection + quick mode SA on the initiator | not a packet capture; the observed cipher is the one negotiated in the SA |

## Operational dependencies

- **Domain controllers** must stay exempt (default): Kerberos, DNS and Group Policy happen before any SA.
- **Secure subnets** bound where negotiation is required; administrative access from outside them (e.g. a bastion) stays in clear.
- **Group changes** take effect only after the machine ticket is renewed; existing SAs remain valid until they expire or are removed.
- The administration workstation must be authorized by restricted servers (access or management group) to verify them over WinRM.
