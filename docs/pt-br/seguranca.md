# Modelo de segurança e limites

[English](../en/security.md)

## Identidade e autoridade

- O Studio não tem usuários, senhas ou tokens próprios. Toda ação usa a **identidade Windows do processo** — a do administrador que o executou. É o mesmo modelo de confiança do GPMC.
- O serviço escuta apenas em `127.0.0.1`; requisições com outro `Host`, outra origem ou sem o token da página são recusadas. Isso protege contra páginas de terceiros no mesmo navegador, não transforma o Studio num serviço de rede. **Não publique a porta.**
- Uma instância com `Deployment:Enabled=false` só lê o AD e planeja; útil para analistas sem permissão de escrita.

## O que o Studio grava

- GPOs com nome `IpsecStudio-*` e descrição contendo o identificador e a versão do plano, vinculadas no destino escolhido. Nenhuma GPO existente é alterada; a reversão remove apenas GPOs com a marca da versão.
- Nenhuma regra de firewall, nenhuma alteração em grupos do AD, nenhum agente nos hosts.
- Nos hosts, via WinRM e só a pedido: `klist -li 0x3e7 purge`, `gpupdate`, remoção de SAs, leitura de regras ativas, conexões TCP de sonda.

## Como o script é construído

O compilador é código constante mais **um único literal JSON** com valores tipados e validados (SIDs no padrão de domínio, nomes de grupo com padrão restrito, IPv4/CIDR, catálogo fechado de suítes). Nenhum texto livre chega ao PowerShell. O hash SHA-256 do roteiro é exibido antes da execução e conferido no momento de aplicar/reverter; um plano reaberto com outra versão é recusado.

## O que o Studio prova e o que não prova

| Evidência | Fonte | Limite |
|---|---|---|
| Plano salvo | disco | intenção, não política |
| GPO criada e vinculada | saída do script, GUIDs | não significa aplicada nos hosts |
| Política efetiva | regras ativas com status, GPO de origem, lista de autorização | um host inacessível fica sem verificação |
| Tráfego | conexão TCP + SA de quick mode no iniciador | não é captura de pacotes; a cifra observada é a negociada na SA |

## Dependências operacionais

- **Controladores de domínio** devem ficar isentos (padrão): Kerberos, DNS e Group Policy acontecem antes de qualquer SA.
- **Sub-redes seguras** limitam onde a negociação é exigida; acesso administrativo vindo de fora delas (por exemplo um bastion) continua em claro.
- **Mudanças de grupo** só valem após renovar o ticket de máquina; SAs existentes continuam válidas até expirar ou serem removidas.
- A estação de administração precisa estar autorizada pelos servidores restritos (grupo de acesso ou gestão) para verificá-los por WinRM.
