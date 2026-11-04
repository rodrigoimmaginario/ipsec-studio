# Windows-only resource check. Reads icons; never executes the application or calls AD.
param(
    [string]$Executable=(Join-Path $PSScriptRoot '..\..\artifacts\app\IpsecStudio.exe'),
    [string]$IconPath=(Join-Path $PSScriptRoot '..\..\app\IpsecStudio\Assets\IpsecStudio.ico')
)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$temp=Join-Path ([IO.Path]::GetTempPath()) ('ipsec-icon-check-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp|Out-Null
$actualIcon=$null;$actual=$null;$expected=$null;$pngStream=$null
try {
    $copy=Join-Path $temp 'IpsecStudio.exe';Copy-Item -LiteralPath (Resolve-Path $Executable).Path -Destination $copy
    $actualIcon=[Drawing.Icon]::ExtractAssociatedIcon($copy)
    if($null -eq $actualIcon){throw 'Executable has no extractable native icon.'}
    $actual=$actualIcon.ToBitmap()
    # Decode the matching PNG frame directly: System.Drawing.Icon.ToBitmap can misread PNG-compressed ICOs.
    $bytes=[IO.File]::ReadAllBytes((Resolve-Path $IconPath).Path);$frame=$null
    for($n=0;$n -lt [BitConverter]::ToUInt16($bytes,4);$n++){
        $entry=6+16*$n;$size=if($bytes[$entry] -eq 0){256}else{[int]$bytes[$entry]}
        if($size -eq $actual.Width){$frame=$entry;break}
    }
    if($null -eq $frame){throw 'ICO does not contain the extracted native size.'}
    $length=[BitConverter]::ToUInt32($bytes,$frame+8);$offset=[BitConverter]::ToUInt32($bytes,$frame+12)
    if($offset+$length -gt $bytes.Length){throw 'ICO frame is out of bounds.'}
    $png=New-Object byte[] $length;[Array]::Copy($bytes,$offset,$png,0,$length)
    $pngStream=[IO.MemoryStream]::new($png,$false);$expected=[Drawing.Bitmap]::new($pngStream)
    if($actual.Width -ne $expected.Width -or $actual.Height -ne $expected.Height){throw 'Native icon size mismatch.'}
    $differences=0
    for($y=0;$y -lt $actual.Height;$y++){for($x=0;$x -lt $actual.Width;$x++){
        $a=$actual.GetPixel($x,$y);$e=$expected.GetPixel($x,$y)
        if(($a.A -ne $e.A) -or (($a.A -gt 0 -or $e.A -gt 0) -and ($a.R -ne $e.R -or $a.G -ne $e.G -or $a.B -ne $e.B))){$differences++}
    }}
    if($differences -gt 0){throw "Native EXE icon differs from the approved ICO in $differences pixels."}
    Write-Host "PASS native executable icon: $($actual.Width)x$($actual.Height), matches approved ICO pixel for pixel."
} finally {
    foreach($resource in @($actual,$expected,$actualIcon,$pngStream)){if($null -ne $resource){$resource.Dispose()}}
    Remove-Item $temp -Recurse -Force
}
