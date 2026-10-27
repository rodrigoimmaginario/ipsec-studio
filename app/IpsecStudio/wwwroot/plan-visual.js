// Presentation only: policy profiles are the primary nodes; AD groups are membership, not profile names.
'use strict';
const PlanVisual = (() => {
  const html = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const button = (label,page) => `<button type="button" class="btn soft" data-action="go" data-page="${page}">${html(label)}</button>`;
  const unique = values => [...new Set(values)];
  function design(d,inventory,assessment){
    const types=[
      ['1','Restrito por grupo',`${d.segments.length} grupo(s)`, 'segments','Quem pode chegar a quais máquinas; cifra opcional.'],
      ['2','IPsec autenticado',`${assessment?.baseComputerIds?.length??'—'} máquinas`, 'baseline','Autenticação na base; saída pode ter fallback.'],
      ['3','Sem IPsec',`${d.exceptions.filter(e=>e.included&&e.kind==='exemption').length} exceção(ões)`, 'exceptions','Bypass declarado; firewall local decide serviços.'],
      ['4','Fronteira',`${d.exceptions.filter(e=>e.included&&e.kind==='boundary').length} fronteira(s)`, 'exceptions','Recebe pares com e sem IPsec.'],
      ['5','Quarentena',`${(d.quarantines||[]).filter(q=>q.included).length} alvo(s)`, 'exceptions','Bloqueio pretendido; gestão a preservar.']
    ];
    const conflicts=assessment?.quarantines?.flatMap(q=>q.conflicts)||[];
    return `<div class="heading"><div><p class="eyebrow">02 / DESENHAR</p><h1>Que perfil cada grupo de máquinas precisa?</h1><p class="subtitle">Escolha um perfil e associe grupos ou endereços. O desenho ainda não altera o domínio.</p></div>${button('Avaliar acessos →','impact')}</div>
      <div class="simple-profiles">${types.map(([n,title,count,page,description])=>`<article class="simple-profile p${n}"><span class="profile-number">${n}</span><h2>${title}</h2><strong>${html(count)}</strong><p>${description}</p>${button('Configurar',page)}</article>`).join('')}</div>
      ${conflicts.length?`<div class="notice red" role="alert">${conflicts.length} conflito(s) entre quarentena e exceção por IP. A precedência real ainda não foi testada.</div>`:''}
      <p class="screen-note">WSUS, impressoras e legados sem IPsec são perfil 3, não fronteira. Cores indicam tipo, nunca estado de implantação.</p>`;
  }
  function review(d,inventory,a,meta){
    const name=id=>inventory.objects.find(o=>o.id===id)?.name||id;
    const names=ids=>unique(ids).map(name);
    const machines=ids=>ids.length?`<ul class="diagram-machines">${names(ids).slice(0,16).map(n=>`<li>${html(n)}</li>`).join('')}${unique(ids).length>16?`<li>+${unique(ids).length-16} outras</li>`:''}</ul>`:'<span class="muted">Nenhuma máquina resolvida</span>';
    if(!Array.isArray(a.profiles))return '<div class="notice red" role="alert">Projeção de perfis indisponível. Reavalie o plano antes de confirmar.</div>';
    const profiles=a.profiles;
    const restrictedProfiles=profiles.filter(p=>p.kind==='restricted');
    const restrictedPaths=restrictedProfiles.flatMap(p=>p.paths);
    const restricted=unique(restrictedProfiles.flatMap(p=>p.computerIds));
    const baseOnly=profiles.find(p=>p.kind==='authenticated')?.computerIds||[];
    const encrypted=profiles.find(p=>p.key==='restricted-encrypted')?.paths||[];
    const authenticated=profiles.find(p=>p.key==='restricted-authenticated')?.paths||[];
    const legacy=restrictedPaths.filter(s=>s.scope!=='Any');
    const quarantine=profiles.find(p=>p.kind==='quarantine');
    const conflicts=a.quarantines.flatMap(q=>q.conflicts);
    const overlapping=restricted.filter(id=>restrictedPaths.filter(s=>s.targetComputerIds.includes(id)).length>1);
    const mode={preparation:'solicitar entrada e saída; sem isolamento obrigatório',isolation:'exigir entrada; saída pode ter fallback',strict:'exigir entrada e saída'}[d.baseline.mode]||'modo não resolvido';
    function profileBlock(list,title,cls){
      if(!list.length)return '';
      const hosts=unique(list.flatMap(s=>s.targetComputerIds));
      return `<article class="topology-profile ${cls}"><h3>${title}</h3><p>${hosts.length} máquina(s) neste perfil · ${cls==='p1'?'cifra planejada':'autenticação planejada'}</p><div class="topology-hosts">${hosts.map(id=>{
        const rules=list.filter(s=>s.targetComputerIds.includes(id));
        const old=rules.filter(s=>s.scope!=='Any');
        return `<div class="topology-host"><strong>${html(name(id))}</strong>${old.length?`<small class="legacy-label">Recorte antigo: ${html(old.map(s=>s.scope).join(' + '))}</small>`:''}<details><summary>Grupos AD (${rules.length})</summary>${rules.map(s=>`<small>${html(s.targetGroup)} · origens: ${html(s.sourceGroups.join(', ')||'nenhuma')}</small>`).join('')}</details></div>`;
      }).join('')}</div></article>`;
    }
    function links(list,cls,title){
      const bySources=new Map();
      for(const s of list){
        const key=JSON.stringify([[...s.sourceComputerIds].sort(),s.scope]);
        const entry=bySources.get(key)||{sources:s.sourceComputerIds,targets:[],legacy:[]};
        entry.targets.push(...s.targetComputerIds);
        if(s.scope!=='Any')entry.legacy.push(`${names(s.targetComputerIds).join(', ')}: ${s.scope}`);
        bySources.set(key,entry);
      }
      return [...bySources.values()].map(entry=>`<div class="topology-link ${cls}"><span><b>Podem iniciar</b>${machines(entry.sources)}</span><span class="topology-link-arrow"><b>→ ${title}</b>${entry.legacy.length?`<small>Recortes antigos: ${html(entry.legacy.join('; '))}</small>`:'<small>Máquina inteira · firewall local separado</small>'}</span><span><b>Destinos neste perfil</b>${machines(entry.targets)}</span></div>`).join('');
    }
    const exceptions=(kind,cls,title)=>{
      const rules=profiles.find(p=>p.kind===kind)?.paths||[];
      return `<article class="topology-profile ${cls}"><h3>${title}</h3>${rules.length?rules.map(e=>`<p class="topology-declared">${html(e.sourceDeclaration)} <b>→</b> ${html(e.targetDeclaration)}${e.scope==='Any'?'':` <small>Recorte antigo: ${html(e.scope)}</small>`}</p>`).join(''):'<p class="hint">Não configurado</p>'}</article>`;
    };
    return `<div class="heading"><div><p class="eyebrow">04 / CONFIRMAR</p><h1>Perfis e caminhos de acesso</h1><p class="subtitle">${html(d.name)} · ${html(inventory.domain)} · snapshot ${html(new Date(inventory.capturedAt).toLocaleString('pt-BR'))}</p></div>${meta.id?`<span class="badge amber">Versão ${html(meta.version)}</span>`:'<span class="badge amber">Rascunho</span>'}</div>
    <div class="notice amber">Perfis agrupam máquinas pelo <b>comportamento IPsec</b>, não pelo nome do grupo AD ou pela aplicação. Setas são intenção de <b>início</b> de conexão; não provam tráfego, autorização de serviço ou direção inversa.</div>
    ${conflicts.length?`<div class="notice red" role="alert">${conflicts.length} conflito(s) de quarentena com exceção por IP: precedência efetiva não comprovada.</div>`:''}
    ${overlapping.length?`<div class="notice red" role="alert">${unique(overlapping).length} máquina(s) aparecem em mais de um grupo restrito; autorização combinada ainda não resolvida.</div>`:''}
    ${legacy.length?`<div class="notice amber" role="status">${legacy.length} regra(s) antigas por serviço permanecem limitadas. Máquinas podem compartilhar o <b>perfil de cifra</b> sem terem ainda o mesmo alcance por serviço. Nada foi ampliado automaticamente.</div>`:''}
    <figure class="topology card" aria-label="Diagrama dos perfis de máquinas e setas de início de acesso"><figcaption>Domínio ${html(inventory.domain)} · plano proposto, não aplicado</figcaption>
      <div class="topology-main"><div class="topology-domain"><h2>Máquinas do domínio</h2><div class="topology-grid">
        <article class="topology-profile p2"><h3>IPsec autenticado · base</h3><p>${html(mode)}${d.baseline.encryption?' · cifra também exigida na base':''}</p><strong>Máquinas sem perfil restrito adicional</strong>${machines(baseOnly)}<small>A base também pode alcançar os perfis restritos; suas máquinas não são repetidas neste bloco.</small></article>
        ${profileBlock(encrypted,'Servidores restritos com criptografia','p1')}${profileBlock(authenticated,'Servidores restritos autenticados','p2')}
      </div></div><div class="topology-outside"><h2>Perfis especiais</h2>${exceptions('exemption','p3','Sem IPsec · exceção')}${exceptions('boundary','p4','Fronteira mista')}<article class="topology-profile p5"><h3>Quarentena · bloqueio</h3>${quarantine?.paths.length?quarantine.paths.map(q=>`<p class="topology-declared"><b>${html(q.name)}</b>${machines(q.targetComputerIds)}<small>IPs: ${html(q.targetDeclaration||'não confirmados')} · bloquear entrada e saída, preservando gestão</small></p>`).join(''):'<p class="hint">Não configurada</p>'}</article></div></div>
      <section class="topology-connections"><h2>Quem pode iniciar conexão com o perfil restrito?</h2>${encrypted.length?links(encrypted,'p1','IPsec + cifra'):''}${authenticated.length?links(authenticated,'p2','IPsec autenticado'):''}${!restrictedPaths.length?'<p class="hint">Nenhum grupo restrito configurado.</p>':''}<p class="diagram-caption">Cada seta identifica apenas as origens declaradas para seus destinos. Origens diferentes geram setas separadas, mesmo dentro do mesmo perfil. Firewall local e serviço continuam independentes.</p></section>
      <div class="topology-key"><span class="p1">Restrito + cifra</span><span class="p2">Autenticado</span><span class="p3">Sem IPsec</span><span class="p4">Fronteira</span><span class="p5">Bloqueio</span></div>
    </figure>
    <details class="card pad diagram-details"><summary>Ressalvas e dados técnicos (${a.warnings.length})</summary>${a.warnings.map(w=>`<p class="hint">${html(w)}</p>`).join('')}<p class="hint">Snapshot: ${html(inventory.id)} · identidade ${html(inventory.identity)}. Política efetiva, firewall, autorização do serviço e tráfego não coletados.</p></details>
    <section class="card pad diagram-save"><h2>Guardar este desenho</h2><p class="hint">Salvar cria apenas uma versão local do plano. Nenhuma GPO ou regra será aplicada.</p><label class="check"><input id="ack" type="checkbox"><span>Entendo que salvar não implanta proteção.</span></label><button id="save-plan" data-action="save" class="btn primary" disabled>${meta.id?'Salvar nova versão':'Salvar plano no disco'}</button><div class="actions">${button('← Voltar ao desenho','map')}${button('Reavaliar acesso','impact')}${meta.id?'<button class="btn" data-action="export">Exportar JSON salvo</button>':''}</div><button class="btn wide" disabled>Implantação não implementada</button></section>`;
  }
  return {design,review};
})();
