# IPsec Studio

**Server and Domain Isolation with native Windows IPsec — plan, simulate, deploy and verify.**

**Site:** https://rodrigoimmaginario.github.io/ipsec-studio/ · **Download:** [latest release](https://github.com/rodrigoimmaginario/ipsec-studio/releases/latest)

[Versão em português](README.md)

IPsec Studio turns the classic *Server and Domain Isolation* model (IPsec + Group Policy + Active Directory groups) into a guided workflow: describe **five protection profiles** over AD machine groups, see **predicted policy outcomes for fresh negotiations**, with indeterminate cases explicit before touching the domain, generate the GPOs, apply them with your own administrator identity, verify the effective policy on the hosts and **compare real traffic with the simulation**. Everything happens at the network layer; no application changes and no firewall rules.

![Simulation: what Windows will do with each new connection](docs/images/simulation.png)

## The five profiles

| # | Profile | IPsec behavior |
|---|---|---|
| 1 | **Restricted servers** | Only the access group establishes an SA; no clear fallback; optional ESP encryption |
| 2 | **Authenticated base** | Isolated domain: inbound requires IPsec (machine Kerberos, ESP/null), outbound requests |
| 3 | **No IPsec** | Receives no profile GPO; this does not exempt it from other hosts' requirements. Address exemptions are a separate setting |
| 4 | **Boundary** | Accepts clear inbound from peers without IPsec and negotiates with those that can |
| 5 | **Quarantine** | Requires IPsec inbound and authorizes management; protected hosts deny it, subject to address exemptions and cached state. Firewall/EDR remain necessary |

Each eligible managed computer receives **one profile GPO**; out-of-scope, DC and no-policy computers receive none: security-group filtering with *Deny Apply* for every other profile. Machine authorization lives in the **IPsec transport authorization list** (Allow for the access group, Deny for the exclusion group), decided by the responder.

## Workflow

1. **Inventory** — reads computers, groups, OUs and GPOs from the domain (read only).
2. **Profiles** — assigns AD groups to profiles and sets parameters (secure subnets, exemptions, cryptography).
3. **Simulate** — initiator × responder matrix with the predicted outcome: *clear*, *authenticated*, *encrypted* or *blocked*, with an explanation per cell.
4. **Deploy** — compiles the PowerShell script (SHA-256 hash), applies the GPOs with the identity of whoever opened the Studio, refreshes and verifies the hosts over WinRM, probes traffic and rolls back.

![Deployment panel](docs/images/deploy.png)

## Requirements

- Windows Server 2016+ or Windows 10/11 joined to the domain, with RSAT (`ActiveDirectory`, `GroupPolicy`, `NetSecurity` modules).
- An account allowed to create and link GPOs in the chosen scope, with WinRM access to the hosts (refresh, verify, probe).
- Automated-flow targets: PowerShell 5.1, WinRM, NetSecurity and ScheduledTasks; capture also needs compatible tools (`netsh`/`pktmon`). Tested on Windows Server 2022 and Windows 10/11. IPsec existing since Vista/2008 does not mean those systems were validated by the Studio.

## Quick start

> **v0.5.0 — reliability:** revision-based recovery, owned-membership journals, complete verification and read-only first use. [Release notes and upgrade precautions](docs/release-notes/0.5.0.md). Lab-validated; production use is not recommended without a pilot on your network.

Download the zip from the [latest release](https://github.com/rodrigoimmaginario/ipsec-studio/releases/latest), check its SHA-256 against the `.sha256` file published next to it and run `IpsecStudio.exe`. To build from source:

```powershell
# publish (self-contained win-x64)
dotnet publish app/IpsecStudio/IpsecStudio.csproj -c Release -r win-x64 --self-contained true -o artifacts/app
# set domain and port in artifacts/app/appsettings.json, then:
artifacts\app\IpsecStudio.exe
# open http://127.0.0.1:5090/
```

Start with **Check prerequisites**, confirm the domain and plan in read-only mode. To deploy, set `Deployment:Enabled` to `true`, restart and use a pilot group/OU — Domain Computers at the root is broad scope.

The service listens on `127.0.0.1` only and requires the Windows identity of whoever runs it (Negotiate + SID). Do not publish the port or run it as a shared service.

## Documentation

- [Concepts and profile model](docs/en/concepts.md)
- [Installation and configuration](docs/en/install.md)
- [User guide](docs/en/usage.md)
- [Security model and limits](docs/en/security.md)
- [Recovery and rollback](docs/en/recovery.md)
- [Report vulnerabilities](SECURITY.md)
- [TDD guide](docs/en/tdd.md)

Interface language: Portuguese (default) or English, selectable in the header; the choice is remembered.

## Development

```powershell
scripts\run-checks.ps1          # xUnit tests + gated coverage, UI smoke, build
scripts\install-hooks.ps1       # pre-push hook that runs run-checks
scripts\run-checks.ps1 -Publish # same, plus publish
```

The project follows TDD ([guide](docs/en/tdd.md)): write the test in `tests/IpsecStudio.Tests` (or in the UI smoke) before the code. CI runs only on pull requests and tags, on a Linux runner. Changes to the simulator or the compiler require a lab cycle with an *expected × observed* probe. See [CONTRIBUTING.md](CONTRIBUTING.md) and [CHANGELOG.md](CHANGELOG.md).

## Origin

The implemented model is the *Server and Domain Isolation* design by Rodrigo Immaginario for Universidade Vila Velha (2005–2006, Microsoft case study), updated to current Windows connection security rules and cmdlets.

## License

[MIT](LICENSE) © Rodrigo Immaginario
