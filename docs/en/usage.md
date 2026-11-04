# User guide

[Português](../pt-br/uso.md)

## 01 · Inventory

First use **Check prerequisites** and confirm the domain. The suggestion is a DNS suffix, not proof of AD membership. The package starts with deployment disabled.

**Read / refresh inventory** queries the domain (computers with IPv4 and OS, security groups with members and SIDs, OUs, GPOs). Nothing is written. With a plan open, the table shows the profile each computer will receive and its relevant groups.

## 02 · Profiles

- **Authenticated base**: base group (default *Domain Computers*) and mode. *Inbound requires / outbound requests* is classic domain isolation; *require/require* removes any fallback but cuts access outside the secure subnet (not recommended); *request/request* is for preparation.
- **Restricted servers**: one or more profiles, each with a protected group, an optional access group (no group = any authenticated computer) and encryption.
- **No IPsec**: a computer group that receives no policy and exempt addresses (IPv4 or CIDR) with reason and owner.
- **Boundary**: a group that accepts clear inbound.
- **Quarantine**: the exclusion group; optionally the management group, the only one authorized to talk to isolated machines.
- **Settings**: secure subnets, DC and ICMP exemption, link target (domain or OU), cryptographic suite, hosts outside AD for simulation and probing.

Conflicts (a computer in two incompatible profiles) show in red and block compilation.

## 03 · Simulate

The matrix displays pairs for **up to 30 selected machines per query**, even with a larger inventory. Without a selection it uses the first 30; choose others in the list to inspect their pairs. Selection limits the view, not deployment or the complete probe. A complete probe remains costly in large networks: start with a small pilot.

For each initiator → responder pair it shows the predicted outcome. Click a cell to see the reasoning. Below, the effective profile of each computer with its memberships.

## 04 · Deploy

1. **Save plan** creates an immutable version (inventory snapshot included).
2. **Compile** produces the planned GPOs, the apply script, the rollback script and the SHA-256 hash. Read the script before applying.
3. **Apply GPOs** preserves the compiled artifact and runs it with your identity. **Domain Computers at the root is broad scope, not a pilot**: explicit acknowledgement is required, and policy becomes eligible at normal GPO refresh before waves. Choose a dedicated OU/group to limit the pilot. Any pre-existing `IpsecStudio-*` GPO blocks the apply: roll back first.
4. **Refresh policy on hosts** (always reads current AD policy; does not reapply the open revision): machine ticket purge, `gpupdate` and SA cleanup over WinRM.
5. **Verify effective policy**: compares with the open revision (including an older one), checking presence, enablement, filters, modes, authorization, crypto, source and status with intent. Missing/incomplete readings are not healthy. A No-IPsec host is expected to have no Studio rules.
6. **Probe traffic**: from managed hosts to plan responders and declared external targets (not the entire AD outside the pilot), TCP connection and quick mode SA, compared with the simulation (expected × observed). After applying, wait about 2 minutes before probing. A pair that fell back to clear during the rollout may stay clear for a few minutes with a boundary host, which only *requests* IPsec. The simulation describes the steady state.

**Several DCs.** Apply, rollback and waves write on the PDC emulator DC and wait for reachable DCs to confirm the change (up to 3 minutes by default). The wait may end with pending DCs: this is not a guarantee that all received the change. Waves do not advance with pending DCs. Retrying the first wave after resolving a pending GPO rechecks DC evidence without reapplying GPOs. With one DC down, refresh, verify and probe keep working through the others, provided members use more than one DC as DNS. Creating and removing GPOs needs the PDC.
7. **Roll back** removes only the applied/attempted revision's GPOs, even if a newer edit was saved. **Open revision … for recovery** also gives access to old waves. See the [recovery guide](recovery.md).

The execution log keeps each action, identity, version and output.

## Day-to-day operation

- **Quarantine a machine**: add it to the exclusion group and run *Refresh policy on hosts*. No GPO changes.
- **Grant access to a restricted server**: add the computer to the access group; renew the machine ticket (Refresh) or wait for a reboot.
- **New plan version**: save the edit without losing the recovery target. Undo/reconcile earlier waves in reverse order and roll back the applied GPO revision; only then compile/apply the new one.

## Waves, approval and new groups (0.4)

- **New groups** (02 · Profiles → New groups): declare the name and, in 04 · Deploy, use *Prepare groups in AD* (requires an approval note). Then read the inventory again to use them in profiles.
- **Waves** (02 · Profiles → Waves): each wave lists computers per profile group, in order. In 03 · Simulate, the *After wave N* selector shows the matrix with the membership at that point.
- **Apply wave** (04 · Deploy): requires this GPO revision applied and earlier waves accepted; writes memberships, refreshes/verifies wave hosts, waits for stabilization and probes other initiators too, including those that should be refused inbound. Empty, incomplete, inconclusive results or pending DCs do not permit expansion. Undoing an earlier wave does not approve the next: reapply and validate in order.
- **Roll back wave** undoes only memberships recorded as added by it, last wave first. It still works after GPO removal. Interrupted states and missing journals require manual reconciliation; ownership of preexisting members is never assumed.
- **Negative probe**: a failed TCP connection alone does not prove IPsec caused it. Without a positive connection to that responder in the collection, a negative result is inconclusive. Wave decisions also require matching effective policy and nonempty coverage of expected pairs. Verification checks the responding machine name; duplicate addresses or unknown OU inheritance do not produce a conclusive prediction.
- **Approval note**: required to apply GPOs, prepare groups and apply waves; it is kept in the execution log together with the Windows identity.
