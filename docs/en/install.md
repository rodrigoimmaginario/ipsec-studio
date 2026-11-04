# Installation and configuration

[Português](../pt-br/instalacao.md)

## Where to run

On an administration workstation or server **joined to the domain**, with updated RSAT (including `Add-ADGroupMember -DisablePermissiveModify`, available in updated modules since September 2020; `Get-WindowsCapability -Online -Name Rsat.*` or *Features* on Windows Server). The Studio uses the `ActiveDirectory`, `GroupPolicy` and `NetSecurity` PowerShell modules and `powershell.exe` 5.1.

Run it **with your administrator account** and open the page with the same Windows identity (Negotiate; a login prompt may appear). Another local account cannot use the process's authority. Do not run it as a shared service.

Run it with that account, as you would run GPMC. The process inherits that identity to read AD, write GPOs and open WinRM sessions on the hosts. There is no service account and no stored secret.

## Publish

```powershell
dotnet publish app/IpsecStudio/IpsecStudio.csproj -c Release -r win-x64 --self-contained true -o artifacts/app
```

The output is self-contained (.NET 10); no runtime installation is required on the workstation.

## Configure (`appsettings.json`)

| Key | Meaning |
|---|---|
| `Port` | Local port (`127.0.0.1` only) |
| `Directory:Server` | Domain controller to query. Empty = the domain name, and Windows picks an available DC (recommended with several DCs). Creating and removing GPOs still needs the PDC emulator DC |
| `Directory:ExpectedDomain` | Empty by default: the UI proposes the DNS suffix and requires confirmation. An inventory from another domain is refused |
| `Storage:Root` | Folder for versioned plans and the execution log |
| `Deployment:Enabled` | Defaults to `false`: read-only/planning. Edit to `true` and restart to enable AD changes |
| `Deployment:ProbePort` | TCP port used by the probe (default 5985, WinRM) |
| `Deployment:SettleSeconds` | Internal wait before a wave probe (default 120 s, 0–600). Zero is useful for simulated tests, not for proving real steady state |
| `Deployment:OutsideCheck` | Optional `IPv4:port` outside the secure subnet that every verified host must reach, e.g. an update server or external DNS. If it fails, the host shows as unhealthy |

The same keys can come from environment variables (`Deployment__Enabled`, `Storage__Root`, …).

## Run

`scripts\Start-App.ps1` or `IpsecStudio.exe` directly. Open `http://127.0.0.1:<port>/`. Logs in `C:\ProgramData\IpsecStudio\logs`.

## First use

1. Open the page as the operator who started the Studio. For 401/403, check that identity; do not disable authentication or expose the port to work around it.
2. Use **Check prerequisites**: domain membership and RSAT modules. This does not prove AD/remote WinRM permissions.
3. Check and confirm the domain. The selection is stored in `<Storage:Root>/directory.json`; environment variables take precedence. To switch domains, confirm another in the UI.
4. Read inventory, plan and simulate without enabling deployment. Before enabling writes, prepare a pilot and read [recovery](recovery.md).

File logs are produced by `Start-App.ps1`; direct execution writes to the console.

## Prepare the domain

Create the security groups that will represent the profiles (for example `IPSEC-SQL-Servers`, `IPSEC-SQL-Clients`, `IPSEC-Boundary`, `IPSEC-NoPolicy`, `IPSEC-Exclusion`, `IPSEC-Admin`). Include the administration workstation in the access groups of the servers it must manage over the network, or in a management group.

Make sure WinRM is enabled on the hosts (default on Windows Server) for the *Refresh*, *Verify* and *Probe* steps.
