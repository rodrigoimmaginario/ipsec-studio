# Segurança / Security

## Português

O Studio é uma ferramenta administrativa local, não um serviço de rede. Use somente em estação administrativa controlada, como o operador que iniciou o processo. Windows Authentication e comparação de SID restringem a API a esse operador; o token da página protege as gravações contra origens de navegador indevidas. Não publique a porta nem encaminhe suas credenciais a terceiros.

Para relatar uma vulnerabilidade, use **Security → Report a vulnerability** neste repositório, se a opção estiver disponível. Caso contrário, abra uma issue **sem os detalhes da vulnerabilidade**, pedindo um canal privado. Não publique credenciais, inventários, planos ou endereços reais de terceiros.

Informe versão/hash do binário, cenário mínimo, resultado esperado/observado e impacto. A manutenção é voluntária, sem SLA. Uma release anterior não recebe automaticamente os ajustes presentes no código ainda não publicado.

Veja o [modelo de segurança](docs/pt-br/seguranca.md) e o [guia de recuperação](docs/pt-br/recuperacao.md). Avalie primeiro em laboratório e piloto limitado; nenhum teste substitui validação na sua rede.

## English

The Studio is a local administrative tool, not a network service. Use it only on a controlled administration workstation, as the operator who started the process. Windows Authentication and SID comparison restrict the API to that operator; the page token protects writes against unwanted browser origins. Do not expose the port or forward your credentials to others.

To report a vulnerability, use **Security → Report a vulnerability** in this repository if available. Otherwise, open an issue **without vulnerability details**, asking for a private channel. Do not publish credentials, inventories, plans or actual third-party addresses.

Include binary version/hash, a minimal scenario, expected/observed behavior and impact. Maintenance is voluntary, with no SLA. An older release does not automatically receive changes in unpublished source.

See the [security model](docs/en/security.md) and [recovery guide](docs/en/recovery.md). Evaluate in a lab and limited pilot first; no test replaces validation on your network.
