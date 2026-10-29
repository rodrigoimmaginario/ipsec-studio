// One-off patch for CV4.DS4: packet capture around the probe. Safe to delete after use.
import fs from 'node:fs';
const f = 'app/IpsecStudio/Deployment.cs';
let s = fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n');
const rep = (a, b) => { if (!s.includes(a)) throw new Error('missing: ' + a.slice(0, 80)); s = s.replace(a, () => b); };

rep(`public sealed record HostRequest(string PlanId, int Version, string Action, string[]? ComputerIds = null);`,
String.raw`public sealed record HostRequest(string PlanId, int Version, string Action, string[]? ComputerIds = null) {
    // Probe only: capture packets on the responders while probing (NDIS trace), keep counts, delete the capture.
    public bool Capture { get; init; }
}`);
rep(`public sealed record ProbeResult(string Initiator, string Responder, string Expected, string Observed, bool Match, string Detail);`,
String.raw`public sealed record ProbeResult(string Initiator, string Responder, string Expected, string Observed, bool Match, string Detail) {
    public WireObservation? Wire { get; init; }
}`);
rep(`        return await RunProbe(plan, await AppliedMembership(plan), request.ComputerIds, identity);
    }
    async Task<ProbeResult[]> RunProbe(SavedPlan plan, InventorySnapshot membership, string[]? computerIds, string identity) {`,
`        return await RunProbe(plan, await AppliedMembership(plan), request.ComputerIds, identity, request.Capture);
    }
    async Task<ProbeResult[]> RunProbe(SavedPlan plan, InventorySnapshot membership, string[]? computerIds, string identity, bool capture = false) {`);
rep(`        var (ok, output) = await runner.Run(ProbeScript.Replace("__SPEC__", JsonSerializer.Serialize(spec)), TimeSpan.FromMinutes(10));
        await repository.AppendDeployment(new(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, "", "probe", ok, output));
        if (!ok) throw new InvalidOperationException(L.T("deploy.probeFailed", output));
        var raw = JsonSerializer.Deserialize<RawProbe[]>(output, Json.Settings) ?? [];
        return raw.Select(p => {
            var expected = p.Error is not null ? "indeterminate" : Simulator.Evaluate(plan.Input, membership, a, p.InitiatorId, p.ResponderId).Outcome;
            var observed = Classify(p.Tcp, p.Cipher, p.Error);
            return new ProbeResult(p.Initiator, p.Responder, expected, observed, expected == observed, p.Error ?? $"tcp={p.Tcp} cipher={p.Cipher ?? "-"} integrity={p.Integrity ?? "-"} auth={p.Auth ?? "-"}");
        }).ToArray();
    }`,
String.raw`        // Capture on domain responders other than the Studio host itself; hosts outside the domain are never captured.
        var captureHosts = capture ? a.Hosts.Where(h => h.Profile != "dc" && h.Ip is not null && !h.Name.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) && System.Text.RegularExpressions.Regex.IsMatch(h.Name, @"\A[A-Za-z0-9-]{1,63}\z"))
            .Select(h => new { name = h.Name, fqdn = h.Name + "." + plan.Inventory.Domain }).ToArray() : [];
        var started = Array.Empty<string>();
        if (captureHosts.Length > 0) {
            var (sok, sout) = await runner.Run(CaptureStartScript.Replace("__HOSTS__", JsonSerializer.Serialize(captureHosts)), TimeSpan.FromMinutes(5));
            await repository.AppendDeployment(new(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, "", "capture-start", sok, sout));
            if (sok) started = (JsonSerializer.Deserialize<CaptureHost[]>(sout, Json.Settings) ?? []).Where(h => h.Ok).Select(h => h.Host).ToArray();
        }
        string output; CaptureHost[] captured = [];
        try {
            var (ok, o) = await runner.Run(ProbeScript.Replace("__SPEC__", JsonSerializer.Serialize(spec)), TimeSpan.FromMinutes(10));
            output = o;
            await repository.AppendDeployment(new(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, "", "probe", ok, output));
            if (!ok) throw new InvalidOperationException(L.T("deploy.probeFailed", output));
        } finally {
            if (started.Length > 0) {
                var stopSpec = new { hosts = captureHosts.Where(h => started.Contains(h.name)), port = ProbePort };
                var (tok, tout) = await runner.Run(CaptureStopScript.Replace("__SPEC__", JsonSerializer.Serialize(stopSpec)), TimeSpan.FromMinutes(10));
                await repository.AppendDeployment(new(DateTimeOffset.UtcNow, identity, plan.Id, plan.Version, "", "capture-stop", tok, tout));
                if (tok) captured = (JsonSerializer.Deserialize<CaptureHost[]>(tout, Json.Settings) ?? []).Where(h => h.Ok).ToArray();
            }
        }
        var ips = a.Hosts.Where(h => h.Ip is not null).ToDictionary(h => h.ComputerId, h => h.Ip!);
        var raw = JsonSerializer.Deserialize<RawProbe[]>(output, Json.Settings) ?? [];
        return raw.Select(p => {
            var expected = p.Error is not null ? "indeterminate" : Simulator.Evaluate(plan.Input, membership, a, p.InitiatorId, p.ResponderId).Outcome;
            var observed = Classify(p.Tcp, p.Cipher, p.Error);
            var result = new ProbeResult(p.Initiator, p.Responder, expected, observed, expected == observed, p.Error ?? $"tcp={p.Tcp} cipher={p.Cipher ?? "-"} integrity={p.Integrity ?? "-"} auth={p.Auth ?? "-"}");
            var host = captured.FirstOrDefault(h => h.Host.Equals(p.Responder, StringComparison.OrdinalIgnoreCase));
            if (host is null || !ips.TryGetValue(p.InitiatorId, out var from) || !ips.TryGetValue(p.ResponderId, out var to)) return result;
            var (esp, clear) = Wire.Count(host.Buckets ?? [], from, to, ProbePort);
            return result with { Wire = new(esp, clear, Wire.Judge(expected, esp, clear)) };
        }).ToArray();
    }`);
rep(`    const string ProbeScript = """`,
String.raw`    // Starts an NDIS packet capture on each responder. Refuses a host where another trace is already running.
    const string CaptureStartScript = """
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$hosts=ConvertFrom-Json @'
__HOSTS__
'@
$results=@()
foreach($h in @($hosts)){
  try{
    [void](Invoke-Command -ComputerName $h.fqdn -ScriptBlock {
      $dir='C:\ProgramData\IpsecStudio\capture'; New-Item -ItemType Directory -Path $dir -Force | Out-Null
      $status=(netsh.exe trace show status) -join ' '
      if($status -notmatch 'no trace session'){ throw "another trace session is running" }
      Remove-Item (Join-Path $dir 'probe.*') -Force -ErrorAction SilentlyContinue
      $out=netsh.exe trace start capture=yes report=no persistent=no maxsize=128 overwrite=yes tracefile=(Join-Path $dir 'probe.etl')
      if($LASTEXITCODE -ne 0){ throw "netsh trace start failed: $($out -join ' ')" }
    })
    $results+=@{host=$h.name;ok=$true;error=$null;bytes=0;sha256=$null;buckets=@()}
  }catch{ $results+=@{host=$h.name;ok=$false;error=$_.Exception.Message;bytes=0;sha256=$null;buckets=@()} }
}
ConvertTo-Json @($results) -Compress -Depth 4
""";
    // Stops the capture, decodes it with pktmon, buckets IPv4 packets (source, destination, port, esp/tcp) and deletes the files.
    const string CaptureStopScript = """
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$spec=ConvertFrom-Json @'
__SPEC__
'@
$results=@()
foreach($h in @($spec.hosts)){
  try{
    $r=Invoke-Command -ComputerName $h.fqdn -ScriptBlock {
      $dir='C:\ProgramData\IpsecStudio\capture'; $etl=Join-Path $dir 'probe.etl'; $txt=Join-Path $dir 'probe.txt'
      try{
        netsh.exe trace stop | Out-Null
        if(-not (Test-Path $etl)){ throw 'capture file missing' }
        $bytes=(Get-Item $etl).Length; $sha=(Get-FileHash $etl -Algorithm SHA256).Hash.ToLowerInvariant()
        pktmon.exe etl2txt $etl --out $txt | Out-Null
        if($LASTEXITCODE -ne 0){ throw 'pktmon etl2txt failed' }
        $b=@{}
        foreach($line in [IO.File]::ReadLines($txt)){
          $m=[regex]::Match($line,'length \d+: (\d+\.\d+\.\d+\.\d+)(?:\.(\d+))? > (\d+\.\d+\.\d+\.\d+)(?:\.(\d+))?: (.*)$')
          if(-not $m.Success){ continue }
          $rest=$m.Groups[5].Value
          $kind=if($rest -match '^(ESP|ip-proto-50)'){'esp'}elseif($rest -match '^Flags \['){'tcp'}else{$null}
          if(-not $kind){ continue }
          $port=if($kind -eq 'tcp' -and $m.Groups[4].Success){[int]$m.Groups[4].Value}else{0}
          $key=$m.Groups[1].Value+'|'+$m.Groups[3].Value+'|'+$port+'|'+$kind
          $b[$key]=1+[int]$b[$key]
        }
        $buckets=@($b.GetEnumerator() | ForEach-Object { $p=$_.Key -split '\|'; @{source=$p[0];destination=$p[1];port=[int]$p[2];kind=$p[3];count=[int]$_.Value} })
        @{ok=$true;error=$null;bytes=$bytes;sha256=$sha;buckets=$buckets}
      }catch{ @{ok=$false;error=$_.Exception.Message;bytes=0;sha256=$null;buckets=@()} }
      finally{ Remove-Item $etl,$txt -Force -ErrorAction SilentlyContinue; Remove-Item (Join-Path $dir 'probe*.cab') -Force -ErrorAction SilentlyContinue }
    }
    $results+=@{host=$h.name;ok=[bool]$r.ok;error=$r.error;bytes=[long]$r.bytes;sha256=$r.sha256;buckets=@($r.buckets)}
  }catch{ $results+=@{host=$h.name;ok=$false;error=$_.Exception.Message;bytes=0;sha256=$null;buckets=@()} }
}
ConvertTo-Json @($results) -Compress -Depth 6
""";
    const string ProbeScript = """`);
fs.writeFileSync(f, s);
console.log('patched');
