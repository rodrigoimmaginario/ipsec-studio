# Changelog

## 0.3.0 — 2026-10-27

- Five protection profiles as first-class plan entities (restricted servers, authenticated base, no IPsec, boundary, quarantine), plus optional management group.
- Simulator with Windows IPsec semantics (exemptions, modes, responder-side authorization, encryption) and initiator × responder matrix with explanations.
- Compiler to GPOs with connection security rules, crypto/authentication sets and IPsec transport authorization lists; deterministic scripts with SHA-256; security-group filtering with Deny Apply.
- Deployment with the operator's identity: apply, refresh (ticket purge, gpupdate, SA cleanup), verify (rule status, source GPO, authorization list), probe (expected × observed), rollback; append-only execution log.
- Bilingual interface and backend messages (pt-BR default, en), choice persisted.
- Validated end to end in a 7-host lab: 25/25 probe matches across quarantine and boundary configurations.

## 0.1.0 — 2026-10-26

- Read-only Active Directory inventory and versioned plan storage.
