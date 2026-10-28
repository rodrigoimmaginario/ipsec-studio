# Summarizes the Cobertura report produced by `dotnet test --collect:"XPlat Code Coverage"` per source file
# and fails when a gated file is below the threshold. Rules, simulator and compiler are the product's logic; they must stay covered.
param([string]$ResultsDirectory=(Join-Path $PSScriptRoot '..\artifacts\coverage'),[int]$Threshold=85,[string[]]$Gate=@('PlanRules.cs','Simulator.cs','Compiler.cs'))
$ErrorActionPreference='Stop'
$report=Get-ChildItem -Path $ResultsDirectory -Recurse -Filter 'coverage.cobertura.xml' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if(-not $report){throw "No coverage report under $ResultsDirectory"}
[xml]$xml=Get-Content $report.FullName
$files=@{}
foreach($class in $xml.coverage.packages.package.classes.class){
  $name=[IO.Path]::GetFileName($class.filename)
  if($name -like '*.g.cs'){continue}
  if(-not $files.ContainsKey($name)){$files[$name]=@{}}
  foreach($line in $class.lines.line){ $n=[int]$line.number; $hit=[int]$line.hits -gt 0; if(-not $files[$name].ContainsKey($n) -or $hit){$files[$name][$n]=$hit} }
}
$failed=@()
Write-Host ("{0,-22} {1,8} {2,8}" -f 'file','lines','covered')
foreach($name in ($files.Keys | Sort-Object)){
  $total=$files[$name].Count; $covered=@($files[$name].Values | Where-Object {$_}).Count
  $pct=if($total){[math]::Round(100.0*$covered/$total,1)}else{0}
  $mark=if($Gate -contains $name){if($pct -lt $Threshold){$failed+="$name $pct%";'  FAIL gate'}else{'  ok gate'}}else{''}
  Write-Host ("{0,-22} {1,8} {2,7}%{3}" -f $name,$total,$pct,$mark)
}
if($failed.Count){throw "Coverage below $Threshold% for: $($failed -join ', ')"}
Write-Host "Coverage gate passed (>= $Threshold% on $($Gate -join ', '))."
