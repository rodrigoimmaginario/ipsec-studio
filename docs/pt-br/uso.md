# Guia de uso

[English](../en/usage.md)

## 01 · Inventário

**Ler / atualizar inventário** consulta o domínio (computadores com IPv4 e sistema operacional, grupos de segurança com membros e SIDs, OUs, GPOs). Nada é gravado. Com um plano aberto, a tabela mostra o perfil que cada computador receberá e seus grupos relevantes.

## 02 · Perfis

- **Base autenticada**: grupo da base (padrão *Domain Computers*) e modo. *Entrada exige / saída solicita* é o isolamento de domínio clássico; *exige/exige* elimina qualquer fallback; *solicita/solicita* serve para preparação.
- **Servidores restritos**: um ou mais perfis, cada um com grupo protegido, grupo de acesso opcional (sem grupo = qualquer computador autenticado) e cifra.
- **Sem IPsec**: grupo de computadores que não recebe política e endereços isentos (IPv4 ou CIDR) com justificativa e responsável.
- **Fronteira**: grupo que aceita entrada em claro.
- **Quarentena**: grupo de exclusão; opcionalmente o grupo de gestão, único autorizado a falar com máquinas isoladas.
- **Parâmetros**: sub-redes seguras, isenção de DCs e ICMP, destino do vínculo (domínio ou OU), suíte criptográfica, hosts fora do AD para simulação e sonda.

Conflitos (computador em dois perfis incompatíveis) aparecem em vermelho e impedem a compilação.

## 03 · Simular

A matriz mostra, para cada par iniciador → destino, o resultado previsto. Clique numa célula para ver o raciocínio. Abaixo, o perfil efetivo de cada computador com suas associações.

## 04 · Implantar

1. **Salvar plano** cria uma versão imutável (snapshot do inventário incluído).
2. **Compilar** gera as GPOs previstas, o script de aplicação, o script de reversão e o hash SHA-256. Leia o script antes de aplicar.
3. **Aplicar GPOs** executa o script com a sua identidade. Qualquer GPO `IpsecStudio-*` pré-existente bloqueia a aplicação: reverta antes.
4. **Atualizar política nos hosts**: purge do ticket de máquina, `gpupdate` e limpeza de SAs via WinRM.
5. **Verificar política efetiva**: regras IPsec ativas (com status), GPO de origem e lista de autorização em cada host. Uma regra com status diferente de OK significa que o host está sem proteção.
6. **Sondar tráfego**: de cada host do domínio para todos os destinos, conexão TCP e SA de quick mode, comparadas à simulação (esperado × observado).
7. **Reverter** remove apenas as GPOs criadas por esta versão (marcadas com o identificador do plano).

O registro de execuções guarda cada ação, identidade, versão e saída.

## Operação do dia a dia

- **Colocar uma máquina em quarentena**: adicione-a ao grupo de exclusão e execute *Atualizar política nos hosts*. Nenhuma GPO muda.
- **Dar acesso a um servidor restrito**: adicione o computador ao grupo de acesso; renove o ticket de máquina (Atualizar) ou aguarde o reinício.
- **Nova versão do plano**: salve, reverta a versão aplicada, compile e aplique a nova.
