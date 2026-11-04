# Recovery and rollback

[Português](../pt-br/recuperacao.md)

## Before the pilot

- Keep VM/server console access or another administrative path independent of the policy being tested. Do not depend on WinRM alone.
- Start with a dedicated OU or group. **Domain Computers at the root is not a limited pilot:** the GPO becomes eligible domain-wide at normal refresh, before any wave.
- Preserve `Storage:Root`: revisions, compiled artifacts and wave journals are part of recovery. Do not publish these files; they contain domain data.
- Keep DCs exempt and check DNS, Kerberos, SYSVOL and replication. Exemption does not guarantee availability if another policy or the network blocks them.

## When the Studio is reachable

1. Check the log: a failed apply may have left partial state.
2. Use **Open revision … for recovery** when a newer edit exists. Reopen the latest edit through *Saved plans* to resume editing; an old revision cannot overwrite a newer one.
3. Roll back waves **last to first**. Only memberships recorded as added by that wave are removed; preexisting members are preserved.
4. Use **Roll back (remove GPOs)**. The target is the applied/attempted revision, not the latest edit. New applications also preserve the compiled script and its hash.
5. Check pending DCs, refresh hosts and verify effective policy. Removing a GPO from AD does not immediately remove cached policy on every host.

If GPOs were already removed, you can still reopen their revision and undo its waves. A new apply is refused while earlier waves are still applied or inconclusive.

## When the Studio is unreachable

1. From a console or administration workstation with RSAT, open GPMC in the correct domain. Check name, GUID, description `IpsecStudio plan <id> v<revision>` and link target. Export a backup/report before changing anything.
2. **Disable/remove the specific GPO link** at the correct target. Do not indiscriminately remove all `IpsecStudio-*` GPOs: another revision/plan or additional links may exist.
3. Wait/check replication and run as administrator from the affected hosts' consoles:

   ```powershell
   gpupdate.exe /target:computer /force /wait:120
   ```

4. Inspect active rules and test the actual service. If old IPsec associations persist, read the SAs and remove only those for the affected pair. Use the console: deleting the administration SA can interrupt your session.
5. Group changes may require renewing the machine ticket or restarting the host. `klist -li 0x3e7 purge` affects SYSTEM-session tickets, including services; assess the impact first.
6. If no independent administrative path exists, restore one using your infrastructure's console/recovery mechanism. The Studio has no out-of-band emergency channel.

## Missing, interrupted or legacy journal

Journals are under `<Storage:Root>/<plan>/waves/`, separated by revision and wave. `pending`/`removing` states, a leftover `.lock` file or a missing journal prevent safe automatic rollback.

- Stop concurrent execution, preserve the files and check memberships on the PDC and other DCs.
- Do not delete the journal or assume an inconclusive state is `added`: that could remove a membership the Studio did not create.
- Reconcile manually with approval and evidence; if ownership cannot be established, undo only memberships whose origin has been proven.
- Waves executed by the published 0.4.0 have no ownership journal. Preexisting membership cannot be reconstructed automatically. Use logs and manual checking; there is no destructive migration.
- Legacy applies without a frozen artifact use a script reconstructed from the revision and still require the exact GPO marker. Review it before running; this is not recovery of the original script bytes.
