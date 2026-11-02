# Changelog

## 0.4.0 — 2026-11-02

Operation: waves, identity, changes outside the Studio, on-the-wire evidence and several domain controllers.

- Waves: ordered membership additions to profile groups, written to AD by the Studio (SID only), with per-wave refresh/verify/probe and automatic stop of expansion on failure; per-wave simulation.
- Group preparation: new security groups created from the plan before being referenced.
- Approval note required for apply, group preparation and wave apply; stored in the execution log with the Windows identity.
- Identity propagation: per-host machine-ticket age versus the last profile-group change ("identity pending"), and per-host renew (ticket purge, gpupdate, SA cleanup).
- Changes outside the Studio: before apply or rollback, the `IpsecStudio-*` GPOs are compared with the compilation (rules, crypto sets, ICMP) and other GPOs with IPsec rules are found by an LDAP search on the IPsec client-side extension (313 GPOs in about 1.4 s instead of 31.4 s), with their reach in the plan. Any difference blocks the action until it is acknowledged; the acknowledgement is logged. A GPO with no active link is listed but does not block.
- On the wire: optional packet capture on the responders during the probe, counting ESP packets, clear connections accepted and clear attempts per pair; the capture is deleted from the host.
- Outbound: restricted and quarantine profiles use Require/Request. Outbound Require is only an explicit option, with a clear warning, because it cuts traffic to hosts without IPsec on the secure subnets. Optional outside-reachability check for verified hosts.
- Rollback refreshes the hosts; the Studio host goes first on apply and last on rollback.
- Several domain controllers: writes pinned to the PDC emulator, wait for GPO and group replication (configurable, 0–3600 s, default 180) and the pending DCs shown; refresh runs as a temporary SYSTEM scheduled task, one host at a time.
- Probe waits about 2 minutes after a change and retries before reporting a clear connection.
- The interface shows the version from the server instead of a fixed number.
- Script runner behind an interface; deployment orchestration covered by unit tests (coverage gate includes Deployment.cs). 105 tests.
- Lab regression (12 machines, 2 DCs, including one DC shut down): PASS.

Known limitations: Active Directory with several sites not tested; legacy IPsec policies (IP Security Policies) not detected; the reach of other GPOs ignores blocked inheritance, enforced links and security filtering; group changes made outside the Studio are not checked against the DC that issued the machine ticket; the package is not code-signed.

## 0.3.0 — 2026-10-27

- Five protection profiles as first-class plan entities (restricted servers, authenticated base, no IPsec, boundary, quarantine), plus optional management group.
- Simulator with Windows IPsec semantics (exemptions, modes, responder-side authorization, encryption) and initiator × responder matrix with explanations.
- Compiler to GPOs with connection security rules, crypto/authentication sets and IPsec transport authorization lists; deterministic scripts with SHA-256; security-group filtering with Deny Apply.
- Deployment with the operator's identity: apply, refresh (ticket purge, gpupdate, SA cleanup), verify (rule status, source GPO, authorization list), probe (expected × observed), rollback; append-only execution log.
- Bilingual interface and backend messages (pt-BR default, en), choice persisted.
- Validated end to end in a 7-host lab: 25/25 probe matches across quarantine and boundary configurations.

## 0.1.0 — 2026-10-26

- Read-only Active Directory inventory and versioned plan storage.
