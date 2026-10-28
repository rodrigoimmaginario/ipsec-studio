# Concepts and profile model

[Português](../pt-br/conceitos.md)

## Server and Domain Isolation

Domain isolation: only trusted computers (domain members authenticated with machine Kerberos) can initiate connections to other trusted computers. Server isolation: inside the isolated domain, some servers accept connections only from an **access group**, optionally encrypted. Both use the same mechanism: IPsec connection security rules distributed through Group Policy, with membership defined by Active Directory security groups.

## Profiles and what they generate

| Profile | GPO | Main rule | Authorization list |
|---|---|---|---|
| Authenticated base | `IpsecStudio-Base` → base group (default *Domain Computers*) | Inbound *Require*, outbound *Request* (configurable) | Allow base group; Deny exclusion group |
| Restricted servers | `IpsecStudio-Restricted-<name>` → protected group | *Require/Require*; encrypted ESP only when encryption is required | Allow protected, access and management groups; Deny exclusion |
| No IPsec | none (Deny Apply everywhere) | — | — |
| Boundary | `IpsecStudio-Boundary` → boundary group | *Request/Request* | none |
| Quarantine | `IpsecStudio-Quarantine` → exclusion group | *Require/Require* | Allow management only |
| Management (optional) | `IpsecStudio-Management` → management group | like the base | Allow base group (no Deny) |

Every GPO also contains: an exemption rule (`InboundSecurity None / OutboundSecurity None`) for domain controllers and declared addresses; a main mode set (default AES-256/SHA-256/DH14); a quick mode set (ESP/null + ESP/AES-256; AES only for encrypted profiles); machine Kerberos authentication; ICMP exemption.

**Filtering.** Each GPO is linked at the domain (or an OU) with *Authenticated Users: Read*, profile group: *Apply*, and **Deny Apply** for the other profiles' groups. A computer therefore receives exactly one profile GPO; quarantine prevails over any other membership.

## How Windows decides (and how the simulator predicts)

1. **The most specific filter wins.** Exempt address (DC, printer, WSUS) → clear traffic under any profile.
2. **Secure subnets.** Outside them no negotiation rule applies → clear.
3. **Both sides' modes.** Initiator without IPsec against a *Require* responder → blocked; responder without IPsec against a *Request* initiator → clear fallback after 3 s; against a *Require* initiator → blocked.
4. **Responder authorization.** If the responder's authorization list does not include (or denies) the initiating machine, negotiation fails and there is no fallback.
5. **Encryption.** If one side only proposes encrypted ESP, the SA is encrypted; otherwise ESP/null (authentication and integrity).

Relevant observations from the lab:

- The authorization list is evaluated **by the side receiving the connection**. A restricted server can initiate connections to any host that authorizes it.
- SAs are **bidirectional** and reused; that is why quarantine has its own GPO and the *Refresh* step clears SAs and tickets after group changes.
- Group changes take effect only after the machine ticket is renewed (`klist -li 0x3e7 purge`) or a reboot; the Studio does this in the *Refresh* step.

## Waves

Wave deployment does not duplicate GPOs: there is always one GPO per profile. A wave controls **who joins each profile group**, as in the original design ("drag and drop" the machine into the group). The Studio writes the membership (by SID, never by name), renews the identity of the wave's hosts and compares the probe with that wave's simulation before allowing the next one.
