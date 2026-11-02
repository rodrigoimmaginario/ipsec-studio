// IPsec Studio site — single diagram model (positions in a 960x540 viewBox). Pure data: labels live in i18n.js.
// Four states shown at once (Rodrigo, 02/11): blocked, allowed (authenticated), encrypted for a group, boundary;
// machines outside the domain are always shown being stopped.
window.SITE_MODEL = {
  nodes: [
    { id: 'unknown', x: 90, y: 110, kind: 'laptop', zone: 'outside' },
    { id: 'visitor', x: 90, y: 440, kind: 'laptop', zone: 'outside' },
    { id: 'domain', x: 340, y: 150, kind: 'pc' },
    { id: 'authorized', x: 340, y: 320, kind: 'pc', zone: 'group' },
    { id: 'app', x: 830, y: 100, kind: 'server' },
    { id: 'db', x: 830, y: 260, kind: 'server', zone: 'restricted' },
    { id: 'wsus', x: 830, y: 425, kind: 'server', zone: 'boundary' },
  ],
  // Shaded areas behind the nodes (labels in i18n.zones). The two outside machines stay out of the isolated network.
  zones: [
    { id: 'isolated', x: 190, y: 26, w: 760, h: 500, style: 'isolated' },
    { id: 'group', x: 250, y: 254, w: 180, h: 140, style: 'group' },
    { id: 'restricted', x: 735, y: 196, w: 196, h: 146, style: 'restricted' },
    { id: 'boundary', x: 735, y: 368, w: 196, h: 136, style: 'boundary' },
  ],
  // Every flow animates at the same time. "note" is an optional short label next to the badge (i18n.notes).
  flows: [
    { from: 'unknown', to: 'app', kind: 'refused', note: 'outside' },
    { from: 'domain', to: 'app', kind: 'auth' },
    { from: 'authorized', to: 'db', kind: 'enc', note: 'group' },
    { from: 'domain', to: 'db', kind: 'refused', note: 'notInGroup', at: 0.55 },
    { from: 'authorized', to: 'wsus', kind: 'auth' },
    { from: 'visitor', to: 'wsus', kind: 'clear', note: 'boundary', at: 0.66 },
  ],
  // "Before" shows the same connection attempts on a network without IPsec: everything reaches everything.
  before: [
    { from: 'unknown', to: 'app', kind: 'clear' }, { from: 'domain', to: 'app', kind: 'clear' }, { from: 'authorized', to: 'db', kind: 'clear' },
    { from: 'domain', to: 'db', kind: 'clear' }, { from: 'authorized', to: 'wsus', kind: 'clear' }, { from: 'visitor', to: 'wsus', kind: 'clear' },
    { from: 'visitor', to: 'db', kind: 'clear' } ],
  states: ['refused', 'auth', 'enc', 'boundary'],
  references: [
    { id: 'case2006', author: 'Microsoft', url: 'referencias/microsoft-case-study-uvv-ipsec-2006.pdf' },
    { id: 'msDomain', author: 'Microsoft Learn', url: 'https://learn.microsoft.com/en-us/windows/security/operating-system-security/network-security/windows-firewall/domain-isolation-policy-design' },
    { id: 'msServer', author: 'Microsoft Learn', url: 'https://learn.microsoft.com/en-us/windows/security/operating-system-security/network-security/windows-firewall/server-isolation-policy-design' },
  ],
  releases: 'https://github.com/rodrigoimmaginario/ipsec-studio/releases/latest',
  docs: {
    pt: { install: 'https://github.com/rodrigoimmaginario/ipsec-studio/blob/main/docs/pt-br/instalacao.md', tests: 'https://github.com/rodrigoimmaginario/ipsec-studio/blob/main/docs/pt-br/tdd.md' },
    en: { install: 'https://github.com/rodrigoimmaginario/ipsec-studio/blob/main/docs/en/install.md', tests: 'https://github.com/rodrigoimmaginario/ipsec-studio/blob/main/docs/en/tdd.md' },
  },
  contributing: 'https://github.com/rodrigoimmaginario/ipsec-studio/blob/main/CONTRIBUTING.md',
  repository: 'https://github.com/rodrigoimmaginario/ipsec-studio',
};
