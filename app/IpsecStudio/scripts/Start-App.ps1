# Starts IPsec Studio with the current Windows identity (the administrator who runs it), listening on 127.0.0.1 only.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$logs='C:\ProgramData\IpsecStudio\logs'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$p=Start-Process -FilePath (Join-Path $root 'IpsecStudio.exe') -WorkingDirectory $root -RedirectStandardOutput (Join-Path $logs 'application.stdout.log') -RedirectStandardError (Join-Path $logs 'application.stderr.log') -PassThru -Wait
exit $p.ExitCode
