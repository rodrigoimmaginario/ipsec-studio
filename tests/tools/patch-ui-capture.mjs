// One-off UI patch for CV4.DS4: capture checkbox and "on the wire" column. Safe to delete after use.
import fs from 'node:fs';
const read = (f) => fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n');
const rep = (s, a, b) => { if (!s.includes(a)) throw new Error('missing: ' + a.slice(0, 70)); return s.replace(a, () => b); };

let i = read('app/IpsecStudio/wwwroot/i18n.js');
i = rep(i, `probeTitle: 'Sonda de tráfego: esperado × observado',`, `capture: 'Capturar pacotes nos destinos durante a sonda', wire: 'No fio', wireClear: 'claro', wireOk: 'confere', wireBad: 'não confere', wireHelp: 'No fio = pacotes capturados no destino durante a sonda: ESP (protegido) e TCP em claro na porta {0}. ESP não diferencia cifra de só integridade; isso vem da SA. A captura é apagada do host; só as contagens ficam.', probeTitle: 'Sonda de tráfego: esperado × observado',`);
i = rep(i, `probeTitle: 'Traffic probe: expected × observed',`, `capture: 'Capture packets on the responders during the probe', wire: 'On the wire', wireClear: 'clear', wireOk: 'matches', wireBad: 'does not match', wireHelp: 'On the wire = packets captured on the responder during the probe: ESP (protected) and clear TCP on port {0}. ESP does not tell encryption from integrity-only; that comes from the SA. The capture is deleted from the host; only counts are kept.', probeTitle: 'Traffic probe: expected × observed',`);
fs.writeFileSync('app/IpsecStudio/wwwroot/i18n.js', i);

let a = read('app/IpsecStudio/wwwroot/app.js');
a = rep(a, `drift: null, ackDrift: false };`, `drift: null, ackDrift: false, capture: false };`);
a = rep(a, "<th>${t('dep.detail')}</th></tr></thead><tbody>${state.probe.map((p) => `<tr class=\"${p.match ? '' : 'mismatch'}\"><td>${html(p.initiator)}</td><td>${html(p.responder)}</td><td class=\"${outcome(p.expected)[1]}\">${outcome(p.expected)[0]}</td><td class=\"${outcome(p.observed)[1]}\">${outcome(p.observed)[0]}</td><td class=\"code\">${html(p.detail)}</td></tr>`).join('')}</tbody></table></div><p class=\"hint\">${t('dep.probeHelp', st.probePort)}</p></div>",
  "${wired ? `<th>${t('dep.wire')}</th>` : ''}<th>${t('dep.detail')}</th></tr></thead><tbody>${state.probe.map((p) => `<tr class=\"${p.match ? '' : 'mismatch'}\"><td>${html(p.initiator)}</td><td>${html(p.responder)}</td><td class=\"${outcome(p.expected)[1]}\">${outcome(p.expected)[0]}</td><td class=\"${outcome(p.observed)[1]}\">${outcome(p.observed)[0]}</td>${wired ? `<td>${p.wire ? `ESP ${p.wire.esp} · ${t('dep.wireClear')} ${p.wire.clear}${p.wire.match === null ? '' : ` <span class=\"badge ${p.wire.match ? 'teal' : 'red'}\">${t(p.wire.match ? 'dep.wireOk' : 'dep.wireBad')}</span>`}` : '—'}</td>` : ''}<td class=\"code\">${html(p.detail)}</td></tr>`).join('')}</tbody></table></div><p class=\"hint\">${t('dep.probeHelp', st.probePort)}</p>${wired ? `<p class=\"hint\">${t('dep.wireHelp', st.probePort)}</p>` : ''}</div>");
a = rep(a, "  const probe = state.probe ?", "  const wired = !!state.probe && state.probe.some((p) => p.wire);\n  const probe = state.probe ?");
a = rep(a, "<button class=\"btn\" data-action=\"probe\" ${state.busy ? 'disabled' : ''}>${t('dep.probe')}</button>", "<button class=\"btn\" data-action=\"probe\" ${state.busy ? 'disabled' : ''}>${t('dep.probe')}</button><label class=\"check\"><input type=\"checkbox\" id=\"capture\" ${state.capture ? 'checked' : ''}><span>${t('dep.capture')}</span></label>");
a = rep(a, "  const ackDrift = $('#ack-drift');", "  const capture = $('#capture'); if (capture) capture.addEventListener('change', () => { state.capture = capture.checked; });\n  const ackDrift = $('#ack-drift');");
a = rep(a, "state.probe = await api('/api/probe', { planId: state.saved.id, version: state.saved.version, action: 'probe' });", "state.probe = await api('/api/probe', { planId: state.saved.id, version: state.saved.version, action: 'probe', capture: state.capture });");
fs.writeFileSync('app/IpsecStudio/wwwroot/app.js', a);

let p = read('tests/browser/prototype-render.mjs');
p = rep(p, "assert.ok(main.innerHTML.includes('identidade pendente')", "vm.runInContext(\"state.probe = [{ initiator: 'app1', responder: 'db1', expected: 'encrypted', observed: 'encrypted', match: true, detail: 'x', wire: { esp: 12, clear: 0, match: true } }]; render()\", context);\nassert.ok(main.innerHTML.includes('No fio') && main.innerHTML.includes('ESP 12 · claro 0') && main.innerHTML.includes('confere') && main.innerHTML.includes('id=\"capture\"'), 'probe wire column and capture option');\nassert.ok(main.innerHTML.includes('identidade pendente')");
fs.writeFileSync('tests/browser/prototype-render.mjs', p);
console.log('patched');
