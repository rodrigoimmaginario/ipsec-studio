# User guide

[Português](../pt-br/uso.md)

## 01 · Inventory

**Read / refresh inventory** queries the domain (computers with IPv4 and OS, security groups with members and SIDs, OUs, GPOs). Nothing is written. With a plan open, the table shows the profile each computer will receive and its relevant groups.

## 02 · Profiles

- **Authenticated base**: base group (default *Domain Computers*) and mode. *Inbound requires / outbound requests* is classic domain isolation; *require/require* removes any fallback; *request/request* is for preparation.
- **Restricted servers**: one or more profiles, each with a protected group, an optional access group (no group = any authenticated computer) and encryption.
- **No IPsec**: a computer group that receives no policy and exempt addresses (IPv4 or CIDR) with reason and owner.
- **Boundary**: a group that accepts clear inbound.
- **Quarantine**: the exclusion group; optionally the management group, the only one authorized to talk to isolated machines.
- **Settings**: secure subnets, DC and ICMP exemption, link target (domain or OU), cryptographic suite, hosts outside AD for simulation and probing.

Conflicts (a computer in two incompatible profiles) show in red and block compilation.

## 03 · Simulate

The matrix shows, for each initiator → responder pair, the predicted outcome. Click a cell to see the reasoning. Below, the effective profile of each computer with its memberships.

## 04 · Deploy

1. **Save plan** creates an immutable version (inventory snapshot included).
2. **Compile** produces the planned GPOs, the apply script, the rollback script and the SHA-256 hash. Read the script before applying.
3. **Apply GPOs** runs the script with your identity. Any pre-existing `IpsecStudio-*` GPO blocks the apply: roll back first.
4. **Refresh policy on hosts**: machine ticket purge, `gpupdate` and SA cleanup over WinRM.
5. **Verify effective policy**: active IPsec rules (with status), source GPO and authorization list on each host. A rule with a status other than OK means the host is unprotected.
6. **Probe traffic**: from every domain host to every responder, TCP connection and quick mode SA, compared with the simulation (expected × observed).
7. **Roll back** removes only the GPOs created by this version (marked with the plan identifier).

The execution log keeps each action, identity, version and output.

## Day-to-day operation

- **Quarantine a machine**: add it to the exclusion group and run *Refresh policy on hosts*. No GPO changes.
- **Grant access to a restricted server**: add the computer to the access group; renew the machine ticket (Refresh) or wait for a reboot.
- **New plan version**: save, roll back the applied version, compile and apply the new one.

## Waves, approval and new groups (0.4)

- **New groups** (02 · Profiles → New groups): declare the name and, in 04 · Deploy, use *Prepare groups in AD* (requires an approval note). Then read the inventory again to use them in profiles.
- **Waves** (02 · Profiles → Waves): each wave lists computers per profile group, in order. In 03 · Simulate, the *After wave N* selector shows the matrix with the membership at that point.
- **Apply wave** (04 · Deploy): writes the membership to AD with your identity, refreshes, verifies and probes only the wave's hosts. If a verification or a probe fails, the wave is marked *failed* and the next one is **blocked** until you roll back or fix it. *Roll back wave* removes the members.
- **Approval note**: required to apply GPOs, prepare groups and apply waves; it is kept in the execution log together with the Windows identity.
