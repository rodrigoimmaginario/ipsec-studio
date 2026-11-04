namespace IpsecStudio;

// Local prerequisite evidence only. It never queries or changes AD and does not claim remote permissions are sufficient.
public static class Preflight {
    public const string Script = """
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$computer=Get-CimInstance Win32_ComputerSystem
$missing=@('ActiveDirectory','GroupPolicy','NetSecurity' | Where-Object { -not (Get-Module -ListAvailable $_) })
@{partOfDomain=[bool]$computer.PartOfDomain;domain=[string]$computer.Domain;missingModules=$missing;ready=([bool]$computer.PartOfDomain -and $missing.Count -eq 0)} | ConvertTo-Json -Compress
""";
}
