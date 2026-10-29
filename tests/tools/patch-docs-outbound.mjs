// One-off docs patch for CV4.DS6 (PT/EN): no outbound Require by default. Safe to delete after use.
import fs from 'node:fs';
const read = (f) => fs.readFileSync(f, 'utf8');
const rep = (f, pairs) => { let s = read(f); for (const [a, b] of pairs) { if (!s.includes(a)) throw new Error(f + ' missing: ' + a.slice(0, 60)); s = s.replace(a, () => b); } fs.writeFileSync(f, s); };
rep('docs/pt-br/conceitos.md', [
  ['| *Require/Require*; só ESP cifrado quando a cifra é exigida |', '| Entrada *Require*, saída *Request*; só ESP cifrado quando a cifra é exigida |'],
  ['| Quarentena | `IpsecStudio-Quarantine` → grupo de exclusão | *Require/Require* |', '| Quarentena | `IpsecStudio-Quarantine` → grupo de exclusão | Entrada *Require*, saída *Request* |'],
  ['3. **Modos dos dois lados.**', `> **Saída *Require* não é usada por padrão.** No laboratório, uma regra com saída *Require* sobre a sub-rede segura fez o Windows bloquear todo o tráfego do computador para fora dela: internet, DNS externo, atualizações e o agente do Azure. *Require/Request* não teve esse efeito. A proteção é decidida na entrada de cada destino. A opção "Exigir IPsec também na saída" existe para restrito e quarentena, e o modo "exigir" da base continua disponível; ambos geram um alerta e não são recomendados. Sem saída *Require*, um computador em quarentena continua bloqueado pelos hosts protegidos, mas alcança em claro os hosts sem IPsec.

3. **Modos dos dois lados.**`]]);
rep('docs/en/concepts.md', [
  ['| *Require/Require*; encrypted ESP only when encryption is required |', '| Inbound *Require*, outbound *Request*; encrypted ESP only when encryption is required |'],
  ['| Quarantine | `IpsecStudio-Quarantine` → exclusion group | *Require/Require* |', '| Quarantine | `IpsecStudio-Quarantine` → exclusion group | Inbound *Require*, outbound *Request* |'],
  ["3. **Both sides' modes.**", `> **Outbound *Require* is not used by default.** In the lab, a rule with outbound *Require* on the secure subnet made Windows block all of the computer's traffic leaving it: internet, external DNS, updates and the Azure agent. *Require/Request* did not. Protection is decided on each responder's inbound side. The "Require IPsec on outbound too" option exists for restricted and quarantine, and the base "require" mode is still available; both raise an alert and are not recommended. Without outbound *Require*, a quarantined computer is still refused by protected hosts but reaches hosts without IPsec in clear.

3. **Both sides' modes.**`]]);
rep('docs/pt-br/uso.md', [['*exige/exige* remove qualquer fallback;', '*exige/exige* remove qualquer fallback, mas corta o acesso fora da sub-rede segura (não recomendado);']]);
rep('docs/en/usage.md', [['*require/require* removes any fallback;', '*require/require* removes any fallback but cuts access outside the secure subnet (not recommended);']]);
rep('README.md', [['| Exige IPsec nos dois sentidos e só autoriza o grupo de gestão;', '| Exige IPsec na entrada e só autoriza o grupo de gestão;']]);
rep('README.en.md', [['| Requires IPsec both ways and authorizes only the management group;', '| Requires IPsec inbound and authorizes only the management group;']]);
console.log('patched');
