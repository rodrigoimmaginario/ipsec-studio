import fs from 'node:fs';
const f = 'app/IpsecStudio/Deployment.cs';
let s = fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n');
const lines = s.split('\n');
const i = lines.findIndex((l) => l.includes('klist.exe -li 0x3e7 tgt'));
if (i < 0) throw new Error('missing');
lines[i] = String.raw`      $tgtRaw=''
      try{ $text=(klist.exe -li 0x3e7 tgt 2>&1 | Out-String); $tgtRaw=$text.Substring(0,[Math]::Min(240,$text.Length)); if($text -match 'Start\s*Time:\s*([^\r\n]+?)\s*\(local\)'){ $tgt=[datetime]::Parse($matches[1],[Globalization.CultureInfo]::CurrentCulture).ToUniversalTime().ToString('o') } }catch{ $tgtRaw="ERR $($_.Exception.Message)" }`;
s = lines.join('\n').replace('tgtStartUtc=$tgt;lastGroupChangeUtc=$lastChangeUtc}', 'tgtStartUtc=$tgt;tgtRaw=$tgtRaw;lastGroupChangeUtc=$lastChangeUtc}');
fs.writeFileSync(f, s);
console.log('ok');
