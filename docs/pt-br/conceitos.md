# Conceitos e modelo de perfis

[English](../en/concepts.md)

## Server and Domain Isolation

Isolamento de domínio: só computadores confiáveis (membros do domínio, autenticados por Kerberos de máquina) conseguem iniciar conexões com outros computadores confiáveis. Isolamento de servidor: dentro do domínio isolado, alguns servidores aceitam conexões apenas de um **grupo de acesso**, opcionalmente com cifra. Os dois usam o mesmo mecanismo: regras de segurança de conexão IPsec distribuídas por Group Policy, com associação definida por grupos de segurança do Active Directory.

## Perfis e o que geram

| Perfil | GPO | Regra principal | Lista de autorização |
|---|---|---|---|
| Base autenticada | `IpsecStudio-Base` → grupo da base (padrão *Domain Computers*) | Entrada *Require*, saída *Request* (configurável) | Allow grupo da base; Deny grupo de exclusão |
| Servidores restritos | `IpsecStudio-Restricted-<nome>` → grupo protegido | *Require/Require*; só ESP cifrado quando a cifra é exigida | Allow grupo protegido, grupo de acesso e gestão; Deny exclusão |
| Sem IPsec | nenhuma (Deny Apply em todas) | — | — |
| Fronteira | `IpsecStudio-Boundary` → grupo de fronteira | *Request/Request* | nenhuma |
| Quarentena | `IpsecStudio-Quarantine` → grupo de exclusão | *Require/Require* | Allow apenas gestão |
| Gestão (opcional) | `IpsecStudio-Management` → grupo de gestão | como a base | Allow grupo da base (sem Deny) |

Todas as GPOs contêm também: regra de isenção (`InboundSecurity None / OutboundSecurity None`) para os controladores de domínio e os endereços declarados; conjunto de main mode (padrão AES-256/SHA-256/DH14); conjunto de quick mode (ESP/null + ESP/AES-256; só AES nos perfis cifrados); autenticação Kerberos de máquina; isenção de ICMP.

**Filtragem.** Cada GPO é vinculada no domínio (ou numa OU) com *Authenticated Users: Read*, grupo do perfil: *Apply*, e **Deny Apply** para os grupos dos outros perfis. Assim, um computador recebe exatamente uma GPO de perfil; a quarentena prevalece sobre qualquer outra associação.

## Como o Windows decide (e como o simulador prevê)

1. **Filtro mais específico vence.** Endereço isento (DC, impressora, WSUS) → tráfego em claro em qualquer perfil.
2. **Sub-redes seguras.** Fora delas nenhuma regra de negociação se aplica → claro.
3. **Modos dos dois lados.** Iniciador sem IPsec contra destino *Require* → bloqueado; destino sem IPsec contra iniciador *Request* → fallback em claro após 3 s; contra iniciador *Require* → bloqueado.
4. **Autorização de quem recebe.** Se a lista de autorização do destino não inclui a máquina iniciadora (ou a nega), a negociação falha e não há fallback.
5. **Cifra.** Se um dos lados só propõe ESP cifrado, a SA é cifrada; caso contrário ESP/null (autenticação e integridade).

Observações relevantes aprendidas em laboratório:

- A lista de autorização é avaliada **pelo lado que recebe a conexão**. Um servidor restrito pode iniciar conexões a qualquer host que o autorize.
- SAs são **bidirecionais** e reaproveitadas; por isso a quarentena tem GPO própria e a etapa *Atualizar* limpa SAs e tickets ao mudar grupos.
- Mudanças de grupo só valem após renovação do ticket de máquina (`klist -li 0x3e7 purge`) ou reinício; o Studio faz isso na etapa *Atualizar*.
