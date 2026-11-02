# Development routine (TDD): write the failing test first, run this, make it green, refactor.
# 1 unit + simulator tests with coverage gate · 2 UI render smoke · 3 build (· 4 publish with -Publish). Fails on the first red step.
param([switch]$Publish)
$ErrorActionPreference='Stop'
$root=Resolve-Path (Join-Path $PSScriptRoot '..')
$dotnet=if($env:DOTNET_EXE){$env:DOTNET_EXE}else{'dotnet'}
$coverage=Join-Path $root 'artifacts\coverage'
if(Test-Path $coverage){Remove-Item $coverage -Recurse -Force}
Write-Host '== 1/3 unit + simulator tests with coverage (tests/IpsecStudio.Tests)'
& $dotnet test (Join-Path $root 'tests\IpsecStudio.Tests\IpsecStudio.Tests.csproj') --nologo -v q --collect:"XPlat Code Coverage" --results-directory $coverage
if($LASTEXITCODE -ne 0){throw 'Unit tests failed'}
& (Join-Path $PSScriptRoot 'coverage.ps1') -ResultsDirectory $coverage
Write-Host '== 2/3 UI render smoke (tests/browser/prototype-render.mjs) and site smoke (tests/site/site-smoke.mjs)'
& node --check (Join-Path $root 'app\IpsecStudio\wwwroot\app.js'); if($LASTEXITCODE -ne 0){throw 'app.js syntax'}
& node --check (Join-Path $root 'app\IpsecStudio\wwwroot\i18n.js'); if($LASTEXITCODE -ne 0){throw 'i18n.js syntax'}
& node (Join-Path $root 'tests\browser\prototype-render.mjs'); if($LASTEXITCODE -ne 0){throw 'UI smoke failed'}
& node (Join-Path $root 'tests\site\site-smoke.mjs'); if($LASTEXITCODE -ne 0){throw 'Site smoke failed'}
Write-Host '== 3/3 build'
& $dotnet build (Join-Path $root 'app\IpsecStudio\IpsecStudio.csproj') --nologo -v q; if($LASTEXITCODE -ne 0){throw 'Build failed'}
if($Publish){
  Write-Host '== 4/4 publish (win-x64, self-contained)'
  & $dotnet publish (Join-Path $root 'app\IpsecStudio\IpsecStudio.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $root 'artifacts\app') --nologo -v q
  if($LASTEXITCODE -ne 0){throw 'Publish failed'}
}
Write-Host 'All checks green.'
