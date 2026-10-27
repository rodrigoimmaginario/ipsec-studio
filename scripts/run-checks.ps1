# Development routine (TDD): write the test first, run this, make it green, refactor.
# Runs the unit/simulator suite, the UI render smoke and the build. Fails on the first red step.
param([switch]$Publish)
$ErrorActionPreference='Stop'
$root=Resolve-Path (Join-Path $PSScriptRoot '..')
$dotnet=if($env:DOTNET_EXE){$env:DOTNET_EXE}else{'dotnet'}
Write-Host '== 1/3 unit + simulator tests (tests/IpsecStudio.Tests)'
& $dotnet run --project (Join-Path $root 'tests\IpsecStudio.Tests\IpsecStudio.Tests.csproj') | Select-String -Pattern '^PASS|"passed"'
if($LASTEXITCODE -ne 0){throw 'Unit tests failed'}
Write-Host '== 2/3 UI render smoke (tests/browser/prototype-render.mjs)'
& node --check (Join-Path $root 'app\IpsecStudio\wwwroot\app.js'); if($LASTEXITCODE -ne 0){throw 'app.js syntax'}
& node --check (Join-Path $root 'app\IpsecStudio\wwwroot\i18n.js'); if($LASTEXITCODE -ne 0){throw 'i18n.js syntax'}
& node (Join-Path $root 'tests\browser\prototype-render.mjs'); if($LASTEXITCODE -ne 0){throw 'UI smoke failed'}
Write-Host '== 3/3 build'
& $dotnet build (Join-Path $root 'app\IpsecStudio\IpsecStudio.csproj') --nologo -v q; if($LASTEXITCODE -ne 0){throw 'Build failed'}
if($Publish){& $dotnet publish (Join-Path $root 'app\IpsecStudio\IpsecStudio.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $root 'artifacts\app') --nologo -v q; if($LASTEXITCODE -ne 0){throw 'Publish failed'}}
Write-Host 'All checks green.'
