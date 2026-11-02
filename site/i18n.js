// IPsec Studio site — texts in PT-BR (default) and EN. Same keys in both languages (checked by tests/site/site-smoke.mjs).
// Node labels may contain "\n" to break into two lines inside the diagram.
window.SITE_I18N = {
  pt: {
    lang: 'pt-BR',
    brand: 'IPsec Studio',
    nav: { how: 'Como funciona', github: 'GitHub' },
    variants: {
      a: { title: 'Você conhece o IPsec — ou ainda se lembra dele?', subtitle: 'Ele nunca saiu do seu Windows e continua sendo uma das defesas mais eficazes da rede interna. Agora, simples de adotar.' },
      b: { title: 'E se o IPsec ainda fosse a melhor defesa que você não está usando?', subtitle: 'Cada conexão autenticada, sem custo de licença, nativo no Windows. O IPsec Studio tira a complicação da implantação.' },
      c: { title: 'Você ainda se lembra do IPsec? Ele ainda vale a pena.', subtitle: 'Zero Trust na sua rede interna, com o que já vem no Windows. Agora, sem complicação.' },
    },
    cta: { how: 'Veja como funciona', download: 'Baixar grátis e colaborar com o projeto' },
    facts: ['Nativo no Windows', 'Sem custo de licença', 'Sem agente nem hardware novo', 'Gratuito e aberto'],
    how: {
      eyebrow: 'Como funciona',
      title: 'Cada conexão precisa se identificar. Quem decide é o destino.',
      intro: 'O IPsec do Windows autentica as máquinas antes de qualquer conexão na rede interna, e pode cifrar o tráfego. As regras vêm do Active Directory, por política de grupo. O IPsec Studio planeja, simula, aplica aos poucos e verifica essas regras para você.',
    },
    controls: { before: 'Antes', after: 'Com IPsec', compare: 'Comparar:' },
    states: {
      refused: { title: 'Bloqueado', text: 'Máquinas fora do domínio não conseguem se conectar. Máquinas do domínio fora do grupo autorizado também não entram nos servidores restritos.' },
      auth: { title: 'Permitido e autenticado', text: 'Máquinas do domínio se identificam com Kerberos e o servidor aceita. Nada muda para o usuário.' },
      enc: { title: 'Cifrado só para o grupo', text: 'Servidores restritos aceitam apenas o grupo autorizado, e o tráfego vai cifrado.' },
      boundary: { title: 'Fronteira', text: 'Servidores como o WSUS atendem os dois mundos: com IPsec para quem tem, em claro para quem ainda não tem.' },
    },
    beforeText: 'Sem IPsec: qualquer máquina conectada à rede alcança qualquer servidor, inclusive as que ninguém conhece.',
    alsoNote: 'Também: controladores de domínio e dispositivos sem IPsec, como impressoras, ficam isentos por endereço; e uma máquina suspeita vai para a quarentena só entrando num grupo.',
    nodes: {
      unknown: 'Notebook\ndesconhecido', visitor: 'Dispositivo\nde visitante', domain: 'Estação do domínio', authorized: 'Estação do\ngrupo autorizado',
      app: 'Servidor de\naplicação', db: 'Banco de dados', wsus: 'WSUS',
    },
    zones: { isolated: 'Rede interna isolada — cada conexão autenticada', group: 'Grupo autorizado', restricted: 'Restrito · cifrado', boundary: 'Fronteira' },
    notes: { outside: 'fora do domínio', group: 'só o grupo', notInGroup: 'fora do grupo', boundary: 'aceito na fronteira' },
    proto: { label: 'Protótipo — abertura:' },
    footer: { free: 'Gratuito e aberto (licença MIT). Sugestões são bem-vindas.', repo: 'Código no GitHub' },
  },
  en: {
    lang: 'en',
    brand: 'IPsec Studio',
    nav: { how: 'How it works', github: 'GitHub' },
    variants: {
      a: { title: 'Do you know IPsec — or still remember it?', subtitle: 'It never left your Windows, and it is still one of the most effective defenses for the internal network. Now, simple to adopt.' },
      b: { title: 'What if IPsec were still the best defense you are not using?', subtitle: 'Every connection authenticated, no license cost, built into Windows. IPsec Studio takes the complexity out of deploying it.' },
      c: { title: 'Remember IPsec? It is still worth it.', subtitle: 'Zero Trust on your internal network, with what already ships in Windows. Now, without the hassle.' },
    },
    cta: { how: 'See how it works', download: 'Download free and contribute to the project' },
    facts: ['Built into Windows', 'No license cost', 'No agent, no new hardware', 'Free and open source'],
    how: {
      eyebrow: 'How it works',
      title: 'Every connection has to identify itself. The responder decides.',
      intro: 'Windows IPsec authenticates machines before any connection on the internal network, and can encrypt the traffic. The rules come from Active Directory through Group Policy. IPsec Studio plans, simulates, applies in steps and verifies those rules for you.',
    },
    controls: { before: 'Before', after: 'With IPsec', compare: 'Compare:' },
    states: {
      refused: { title: 'Blocked', text: 'Machines outside the domain cannot connect. Domain machines outside the authorized group cannot reach restricted servers either.' },
      auth: { title: 'Allowed and authenticated', text: 'Domain machines identify themselves with Kerberos and the server accepts. Nothing changes for the user.' },
      enc: { title: 'Encrypted, group only', text: 'Restricted servers accept only the authorized group, and the traffic is encrypted.' },
      boundary: { title: 'Boundary', text: 'Servers such as WSUS serve both worlds: IPsec for those who have it, clear for those who do not yet.' },
    },
    beforeText: 'Without IPsec: any machine connected to the network reaches any server, including ones nobody knows.',
    alsoNote: 'Also: domain controllers and devices without IPsec, such as printers, are exempted by address; and a suspicious machine goes into quarantine just by joining a group.',
    nodes: {
      unknown: 'Unknown\nlaptop', visitor: 'Visitor\ndevice', domain: 'Domain workstation', authorized: 'Authorized\ngroup workstation',
      app: 'Application\nserver', db: 'Database', wsus: 'WSUS',
    },
    zones: { isolated: 'Isolated internal network — every connection authenticated', group: 'Authorized group', restricted: 'Restricted · encrypted', boundary: 'Boundary' },
    notes: { outside: 'outside the domain', group: 'group only', notInGroup: 'not in the group', boundary: 'accepted at the boundary' },
    proto: { label: 'Prototype — opening:' },
    footer: { free: 'Free and open source (MIT license). Suggestions are welcome.', repo: 'Code on GitHub' },
  },
};
