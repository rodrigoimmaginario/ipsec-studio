# Points git at the versioned hooks folder so the pre-push gate runs for every clone that opts in.
$ErrorActionPreference='Stop'
$root=Resolve-Path (Join-Path $PSScriptRoot '..')
git -C $root config core.hooksPath scripts/hooks
if($LASTEXITCODE -ne 0){throw 'git config failed'}
Write-Host "core.hooksPath = scripts/hooks (pre-push runs scripts/run-checks.ps1). Remove with: git config --unset core.hooksPath"
