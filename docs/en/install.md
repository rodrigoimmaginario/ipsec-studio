# Installation and configuration

[Português](../pt-br/instalacao.md)

## Where to run

On an administration workstation or server **joined to the domain**, with RSAT installed (`Get-WindowsCapability -Online -Name Rsat.*` or *Features* on Windows Server). The Studio uses the `ActiveDirectory`, `GroupPolicy` and `NetSecurity` PowerShell modules and `powershell.exe` 5.1.

Run it **with your administrator account**, as you would run GPMC. The process inherits that identity to read AD, write GPOs and open WinRM sessions on the hosts. There is no service account and no stored secret.

## Publish

```powershell
dotnet publish app/IpsecStudio/IpsecStudio.csproj -c Release -r win-x64 --self-contained true -o artifacts/app
```

The output is self-contained (.NET 10); no runtime installation is required on the workstation.

## Configure (`appsettings.json`)

| Key | Meaning |
|---|---|
| `Port` | Local port (`127.0.0.1` only) |
| `Directory:Server` | Domain controller to query |
| `Directory:ExpectedDomain` | Domain DNS name; an inventory from another domain is refused |
| `Storage:Root` | Folder for versioned plans and the execution log |
| `Deployment:Enabled` | `true` allows apply/rollback; `false` creates a read-only planning instance |
| `Deployment:ProbePort` | TCP port used by the probe (default 5985, WinRM) |

The same keys can come from environment variables (`Deployment__Enabled`, `Storage__Root`, …).

## Run

`scripts\Start-App.ps1` or `IpsecStudio.exe` directly. Open `http://127.0.0.1:<port>/`. Logs in `C:\ProgramData\IpsecStudio\logs`.

## Prepare the domain

Create the security groups that will represent the profiles (for example `IPSEC-SQL-Servers`, `IPSEC-SQL-Clients`, `IPSEC-Boundary`, `IPSEC-NoPolicy`, `IPSEC-Exclusion`, `IPSEC-Admin`). Include the administration workstation in the access groups of the servers it must manage over the network, or in a management group.

Make sure WinRM is enabled on the hosts (default on Windows Server) for the *Refresh*, *Verify* and *Probe* steps.
