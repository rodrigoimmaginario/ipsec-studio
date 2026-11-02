// IPsec Studio site — diagram model (positions in a 960x620 viewBox). Pure data: labels live in i18n.js.
// Outcomes use the product's vocabulary: clear, auth (authenticated), enc (encrypted), refused.
window.SITE_MODEL = {
  nodes: [
    { id: 'unknown', x: 82, y: 190, kind: 'laptop', zone: 'outside' },
    { id: 'domain', x: 270, y: 120, kind: 'pc' },
    { id: 'authorized', x: 270, y: 250, kind: 'pc' },
    { id: 'admin', x: 270, y: 380, kind: 'pc', zone: 'management' },
    { id: 'quarantine', x: 270, y: 515, kind: 'pc', zone: 'quarantine' },
    { id: 'app', x: 830, y: 120, kind: 'server' },
    { id: 'db', x: 830, y: 260, kind: 'server', zone: 'restricted' },
    { id: 'wsus', x: 830, y: 400, kind: 'server', zone: 'boundary' },
    { id: 'dc', x: 540, y: 498, kind: 'server', zone: 'exempt' },
    { id: 'printer', x: 690, y: 498, kind: 'printer', zone: 'exempt' },
  ],
  // Shaded areas drawn behind the nodes (labels in i18n.zones). Only the unknown laptop stays outside the isolated network.
  zones: [
    { id: 'isolated', x: 168, y: 34, w: 782, h: 572, style: 'isolated' },
    { id: 'restricted', x: 730, y: 196, w: 200, h: 120, style: 'restricted' },
    { id: 'boundary', x: 730, y: 336, w: 200, h: 120, style: 'boundary' },
    { id: 'exempt', x: 454, y: 448, w: 330, h: 150, style: 'exempt', label: 'bottom' },
    { id: 'quarantine', x: 186, y: 456, w: 168, h: 142, style: 'quarantine' },
  ],
  scenes: [
    { id: 1, mode: 'before', links: [
      { from: 'unknown', to: 'app', kind: 'clear' }, { from: 'domain', to: 'app', kind: 'clear' }, { from: 'authorized', to: 'db', kind: 'clear' },
      { from: 'quarantine', to: 'db', kind: 'clear' }, { from: 'admin', to: 'quarantine', kind: 'clear' } ] },
    { id: 2, links: [ { from: 'unknown', to: 'app', kind: 'refused' } ] },
    { id: 3, links: [ { from: 'domain', to: 'app', kind: 'auth' } ] },
    { id: 4, links: [ { from: 'authorized', to: 'db', kind: 'enc' }, { from: 'domain', to: 'db', kind: 'refused' } ] },
    { id: 5, links: [ { from: 'domain', to: 'wsus', kind: 'auth' }, { from: 'unknown', to: 'wsus', kind: 'clear' } ] },
    { id: 6, links: [ { from: 'domain', to: 'dc', kind: 'clear' }, { from: 'authorized', to: 'printer', kind: 'clear' } ] },
    { id: 7, links: [ { from: 'quarantine', to: 'app', kind: 'refused' }, { from: 'quarantine', to: 'db', kind: 'refused' }, { from: 'admin', to: 'quarantine', kind: 'auth' } ] },
  ],
  // Before / after overview: the same five connection attempts as scene 1, without and with IPsec.
  overview: {
    before: [
      { from: 'unknown', to: 'app', kind: 'clear' }, { from: 'domain', to: 'app', kind: 'clear' }, { from: 'authorized', to: 'db', kind: 'clear' },
      { from: 'quarantine', to: 'db', kind: 'clear' }, { from: 'admin', to: 'quarantine', kind: 'clear' } ],
    after: [
      { from: 'unknown', to: 'app', kind: 'refused' }, { from: 'domain', to: 'app', kind: 'auth' }, { from: 'authorized', to: 'db', kind: 'enc' },
      { from: 'quarantine', to: 'db', kind: 'refused' }, { from: 'admin', to: 'quarantine', kind: 'auth' } ],
  },
  releases: 'https://github.com/rodrigoimmaginario/ipsec-studio/releases/latest',
  repository: 'https://github.com/rodrigoimmaginario/ipsec-studio',
};
