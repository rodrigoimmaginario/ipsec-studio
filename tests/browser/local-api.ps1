# Windows-only startup/authentication smoke. No AD calls, no GPO/group writes; isolated storage and loopback port.
param([string]$Executable = (Join-Path $PSScriptRoot '..\..\app\IpsecStudio\bin\Debug\net10.0\win-x64\IpsecStudio.exe'),[switch]$RequireStatic)
$ErrorActionPreference='Stop'
$root=Join-Path ([IO.Path]::GetTempPath()) ('ipsec-local-api-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0); $listener.Start(); $port=$listener.LocalEndpoint.Port; $listener.Stop()
$info=[Diagnostics.ProcessStartInfo]::new(); $info.FileName=(Resolve-Path $Executable).Path; $info.UseShellExecute=$false; $info.CreateNoWindow=$true
$info.EnvironmentVariables['Port']=[string]$port; $info.EnvironmentVariables['Storage__Root']=Join-Path $root 'plans'
$info.EnvironmentVariables['Deployment__Enabled']='false'; $info.EnvironmentVariables['Directory__ExpectedDomain']=''
$info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
$p=[Diagnostics.Process]::Start($info); $out=$p.StandardOutput.ReadToEndAsync(); $err=$p.StandardError.ReadToEndAsync()
Add-Type -AssemblyName System.Net.Http
$anonymous=[Net.Http.HttpClient]::new(); $handler=[Net.Http.HttpClientHandler]::new(); $handler.UseDefaultCredentials=$true; $handler.UseProxy=$false
$client=[Net.Http.HttpClient]::new($handler); $client.Timeout=[TimeSpan]::FromSeconds(15)
try {
    $url="http://localhost:$port"
    $response=$null
    for($i=0;$i -lt 60 -and -not $response;$i++){
        if($p.HasExited){ throw ('Studio exited: '+$err.Result+' '+$out.Result) }
        try { $response=$anonymous.GetAsync("$url/api/status").GetAwaiter().GetResult() } catch { Start-Sleep -Milliseconds 200 }
    }
    if(-not $response -or [int]$response.StatusCode -ne 401){ throw 'Anonymous access must return 401.' }
    $response=$client.GetAsync("$url/api/status").GetAwaiter().GetResult()
    if([int]$response.StatusCode -ne 200){ throw ('Operator authentication failed: '+[int]$response.StatusCode+' '+$response.Content.ReadAsStringAsync().Result) }
    $status=$response.Content.ReadAsStringAsync().Result | ConvertFrom-Json
    if($status.deploymentEnabled){ throw 'Smoke must be read-only.' }
    if($RequireStatic){
        foreach($path in @('/','/app.js','/i18n.js','/style.css','/app.css','/brand.svg','/brand-dark.svg','/favicon.svg')){
            $asset=$client.GetAsync($url+$path).GetAwaiter().GetResult()
            if([int]$asset.StatusCode -ne 200){throw "Published asset failed: $path"}
            if($path.EndsWith('.svg')){
                if($asset.Content.Headers.ContentType.MediaType -ne 'image/svg+xml'){throw "SVG content type failed: $path"}
                $source=Join-Path $PSScriptRoot ('..\..\app\IpsecStudio\wwwroot\'+$path.TrimStart('/'))
                $expected=[IO.File]::ReadAllText((Resolve-Path $source).Path).Replace("`r`n","`n")
                if($asset.Content.ReadAsStringAsync().Result.Replace("`r`n","`n") -cne $expected){throw "Published SVG differs from source: $path"}
            }
        }
    }
    $body=[Net.Http.StringContent]::new('{}',[Text.Encoding]::UTF8,'application/json')
    $response=$client.PostAsync("$url/api/inventory/refresh",$body).GetAwaiter().GetResult()
    if([int]$response.StatusCode -ne 403){ throw 'POST without local token must return 403.' }
    # Configuring a domain changes only this isolated local settings file, never AD.
    $client.DefaultRequestHeaders.Add('X-Plan-Token',[string]$status.token)
    $response=$client.PostAsync("$url/api/preflight",[Net.Http.StringContent]::new('{}',[Text.Encoding]::UTF8,'application/json')).GetAwaiter().GetResult()
    if([int]$response.StatusCode -ne 200){ throw 'Read-only prerequisite check failed.' }
    $preflight=$response.Content.ReadAsStringAsync().Result | ConvertFrom-Json
    if($null -eq $preflight.partOfDomain -or $null -eq $preflight.missingModules){ throw 'Prerequisite evidence is incomplete.' }
    $body=[Net.Http.StringContent]::new('{"domain":"audit.example.test"}',[Text.Encoding]::UTF8,'application/json')
    $response=$client.PostAsync("$url/api/directory/configure",$body).GetAwaiter().GetResult()
    if([int]$response.StatusCode -ne 200){ throw ('Domain confirmation failed: '+$response.Content.ReadAsStringAsync().Result) }
    if(-not (Test-Path (Join-Path $root 'plans\directory.json'))){ throw 'Domain confirmation was not persisted locally.' }
    Write-Host 'PASS local API: anonymous 401, operator 200, missing token 403, local domain confirmation, read-only.'
} finally {
    $anonymous.Dispose(); $client.Dispose(); if(-not $p.HasExited){$p.Kill();$p.WaitForExit()}; $p.Dispose()
    Remove-Item $root -Recurse -Force
}
