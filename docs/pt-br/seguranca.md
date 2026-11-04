# Modelo de segurança e limites

[English](../en/security.md)

## Identidade e autoridade

- Toda ação usa a **identidade Windows do processo**, como o GPMC. Não há conta de serviço nem senha própria do Studio.
- O serviço escuta em `127.0.0.1`. Windows Authentication (Negotiate) e comparação do SID exigem o mesmo operador que iniciou o processo, inclusive para ler a API. O navegador pode pedir essa conta Windows.
- O token da página, a validação de Host/origem e a política de conteúdo protegem a fronteira do navegador; não substituem a autenticação Windows. **Não publique a porta.** Use uma instância em estação administrativa controlada, não um serviço compartilhado.
- O pacote inicia com `Deployment:Enabled=false`: ler/planejar não habilita alterações no domínio. Ative conscientemente no arquivo de configuração e reinicie.
- A pasta de armazenamento tem permissões para operador, SYSTEM e administradores locais; esses administradores continuam na fronteira de confiança. Proteja também cópias e backups.

## O que o Studio altera

- GPOs `IpsecStudio-*` com identificador e revisão na descrição; vínculos no destino escolhido. A reversão exige a marca da revisão.
- Grupos novos declarados e associações de computadores em ondas, somente a pedido e com aprovação. Não cria regras de firewall nem instala agente permanente.
- Nos hosts, via WinRM: tarefa SYSTEM temporária para atualizar GPOs, purge de tickets de máquina e limpeza de SAs; leituras de regras, conexões de sonda e captura opcional, apagada ao final quando o host permanece acessível.
- Purge e limpeza de SAs podem afetar serviços e sessões existentes. A sonda não é observação passiva. Evite concorrência com outras intervenções; tenha console independente.

## Construção e recuperação dos roteiros

Dados são serializados em JSON antes de entrar no PowerShell, inclusive contêineres de grupos. Aplicação/reversão conferem o hash da prévia. Novas aplicações preservam o artefato compilado; revisões de edição e de implantação são distintas. Diários de ondas persistem a propriedade das associações; estados ambíguos não são automaticamente desfeitos.

Aprovação é uma nota do operador, não um sistema de aprovação por segunda pessoa. Registros locais são evidência operacional, não um log inviolável contra administradores da máquina. Veja [recuperação](recuperacao.md), inclusive para instalações antigas sem diários/artefatos.

## O que cada evidência significa

| Evidência | O que permite afirmar | Limite |
|---|---|---|
| Plano e matriz | previsão conforme snapshot e escopo modelado | não é descoberta de todos os fluxos nem garantia de aplicação da GPO |
| GPO no AD | configuração criada/vinculada e comparada | não prova que todos os hosts/DCs já a receberam |
| Política efetiva | regras, filtros, modos, autorização, criptografia, origem e status correspondem ao esperado | um host inacessível ou leitura incompleta continua não verificado |
| TCP + SA | conectividade e proteção negociada na sonda | TCP sem conexão não identifica sozinho a causa; SA pode ser reutilizada |
| Captura ESP | pacotes protegidos observados no par | ESP também pode usar cifra nula; distinguir cifra de integridade exige a SA |

## Limites operacionais

- Domain Computers na raiz não é piloto limitado: a GPO fica elegível antes das ondas. Comece por OU/grupo dedicado.
- A previsão presume negociação nova. SAs são bidirecionais: uma SA aberta pela gestão pode permitir o retorno mesmo quando a lista unilateral negaria uma negociação nova. Essa combinação aparece como não determinada, não libera expansão e não é corrigida ampliando permissões. Não use IPsec no lugar do firewall para prometer bloqueio por conexão.
- A matriz usa até 30 máquinas por consulta; o inventário não tem esse limite. A sondagem completa continua cara em redes grandes.
- Endereços e sub-redes configuráveis são IPv4; regra com `Any` não equivale a listar somente prefixos IPv4. Tráfego fora do escopo configurado não está coberto pela promessa de isolamento.
- As GPOs do Studio negam Aplicar GPO aos grupos Domain Controllers e Read-only Domain Controllers, mesmo com associação acidental a um perfil. DCs e endereços isentos, ICMP e fronteira têm exceções deliberadas. Quarentena não substitui contenção de EDR/firewall nem bloqueia todo acesso externo.
- Ticket mais novo que a mudança de grupo é **indício**, não comprovação dos grupos no ticket nem da replicação no DC emissor. A política efetiva e os testes continuam necessários.
- Para os próprios vínculos não impostos, o inventário lê bloqueio de herança nas OUs: bloqueio abaixo do vínculo exclui o host; informação ausente torna a previsão indeterminada. Inventários antigos precisam ser relidos antes de novas ondas; a recuperação de GPO continua disponível.
- Vários sites AD, políticas IPsec legadas, alcance de outras GPOs (herança/vínculos impostos/filtragem adicional), SIDHistory e Windows localizado exigem validação específica. O pacote não tem Authenticode.
- Uma alteração de compilador/simulador/implantação só é homologada após nova regressão no AD. Testes locais e cobertura C# não comprovam sozinhos os comandos PowerShell.
