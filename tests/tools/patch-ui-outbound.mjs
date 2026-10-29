// One-off UI patch for CV4.DS6: outbound Require option with inline alert; alert shown on profiles and deploy pages.
import fs from 'node:fs';
const read = (f) => fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n');
const rep = (s, a, b) => { if (!s.includes(a)) throw new Error('missing: ' + a.slice(0, 70)); return s.replace(a, () => b); };

let i = read('app/IpsecStudio/wwwroot/i18n.js');
i = rep(i, `exemptIcmp: 'Isentar ICMP'`, `exemptIcmp: 'Isentar ICMP', requireOutbound: 'Exigir IPsec também na saída dos perfis restrito e quarentena (não recomendado)', requireOutboundHint: 'Com saída "Require" o Windows bloqueou, no laboratório, todo o tráfego desses computadores para fora da sub-rede segura: internet, DNS externo, atualizações e o agente do Azure. Deixe desmarcado; a proteção continua decidida na entrada de cada destino.'`);
i = rep(i, `exemptIcmp: 'Exempt ICMP'`, `exemptIcmp: 'Exempt ICMP', requireOutbound: 'Require IPsec on outbound too for restricted and quarantine (not recommended)', requireOutboundHint: 'With outbound "Require", Windows blocked in the lab all traffic from these computers leaving the secure subnet: internet, external DNS, updates and the Azure agent. Leave it unchecked; protection is still decided on each responder\\'s inbound side.'`);
i = rep(i, `modeRequire: 'Entrada e saída exigem (sem fallback)'`, `modeRequire: 'Entrada e saída exigem (não recomendado: corta o acesso fora da sub-rede segura)'`);
i = rep(i, `modeRequire: 'Inbound and outbound require (no fallback)'`, `modeRequire: 'Inbound and outbound require (not recommended: cuts access outside the secure subnet)'`);
fs.writeFileSync('app/IpsecStudio/wwwroot/i18n.js', i);

let a = read('app/IpsecStudio/wwwroot/app.js');
a = rep(a, `settings: { secureSubnets: [], exemptDomainControllers: true, exemptIcmp: true,`, `settings: { secureSubnets: [], exemptDomainControllers: true, exemptIcmp: true, requireOutbound: false,`);
a = rep(a, "<span>${t('prof.exemptIcmp')}</span></label>", "<span>${t('prof.exemptIcmp')}</span></label><label class=\"check\"><input type=\"checkbox\" data-field=\"settings.requireOutbound\" ${s.requireOutbound ? 'checked' : ''}><span>${t('prof.requireOutbound')}</span></label>${s.requireOutbound ? `<div class=\"notice red\">${t('prof.requireOutboundHint')}</div>` : ''}");
// The outbound alert is the first warning when present ("ALERTA"/"ALERT"); show it open, not folded among the warnings.
a = rep(a, `function deployScreen() {`, "const outboundAlert = () => { const w = (state.assessment?.warnings ?? []).find((x) => x.startsWith('ALERT')); return w ? `<div class=\"notice red mb\" id=\"outbound-alert\">${html(w)}</div>` : ''; };\nfunction deployScreen() {");
a = rep(a, "<p class=\"eyebrow\">${t('dep.eyebrow')}</p>", "${outboundAlert()}<p class=\"eyebrow\">${t('dep.eyebrow')}</p>");
fs.writeFileSync('app/IpsecStudio/wwwroot/app.js', a);

let p = read('tests/browser/prototype-render.mjs');
p = rep(p, "assert.ok(main.innerHTML.includes('identidade pendente')", "vm.runInContext(\"state.assessment = { ...state.assessment, warnings: ['ALERTA — saída \\\\'Require\\\\' em: Banco de dados.'] }; state.page = 'deploy'; render()\", context);\nassert.ok(main.innerHTML.includes('id=\"outbound-alert\"') && main.innerHTML.includes('ALERTA'), 'outbound alert shown on the deploy page');\nassert.ok(main.innerHTML.includes('identidade pendente')");
fs.writeFileSync('tests/browser/prototype-render.mjs', p);
console.log('patched');
