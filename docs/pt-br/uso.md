# Guia de uso

[English](../en/usage.md)

## 01 · Inventário

Primeiro use **Conferir pré-requisitos** e confirme o domínio. A sugestão é um sufixo DNS, não prova de ingresso no AD. O pacote inicia sem permissão de implantação.

**Ler / atualizar inventário** consulta o domínio (computadores com IPv4 e sistema operacional, grupos de segurança com membros e SIDs, OUs, GPOs). Nada é gravado. Com um plano aberto, a tabela mostra o perfil que cada computador receberá e seus grupos relevantes.

## 02 · Perfis

- **Base autenticada**: grupo da base (padrão *Domain Computers*) e modo. *Entrada exige / saída solicita* é o isolamento de domínio clássico; *exige/exige* elimina qualquer fallback, mas corta o acesso fora da sub-rede segura (não recomendado); *solicita/solicita* serve para preparação.
- **Servidores restritos**: um ou mais perfis, cada um com grupo protegido, grupo de acesso opcional (sem grupo = qualquer computador autenticado) e cifra.
- **Sem IPsec**: grupo de computadores que não recebe política e endereços isentos (IPv4 ou CIDR) com justificativa e responsável.
- **Fronteira**: grupo que aceita entrada em claro.
- **Quarentena**: grupo de exclusão; opcionalmente o grupo de gestão, único autorizado a falar com máquinas isoladas.
- **Parâmetros**: sub-redes seguras, isenção de DCs e ICMP, destino do vínculo (domínio ou OU), suíte criptográfica, hosts fora do AD para simulação e sonda.

Conflitos (computador em dois perfis incompatíveis) aparecem em vermelho e impedem a compilação.

## 03 · Simular

A matriz mostra os pares de **até 30 máquinas selecionadas por consulta**, mesmo num inventário maior. Sem seleção, usa as primeiras 30; escolha outras na lista para conferir seus pares. A seleção limita a visualização, não a implantação nem a sondagem completa. A sondagem completa continua custosa em redes grandes: comece por um piloto pequeno.

Para cada par iniciador → destino, mostra o resultado previsto. Clique numa célula para ver o raciocínio. Abaixo, o perfil efetivo de cada computador com suas associações.

## 04 · Implantar

1. **Salvar plano** cria uma versão imutável (snapshot do inventário incluído).
2. **Compilar** gera as GPOs previstas, o script de aplicação, o script de reversão e o hash SHA-256. Leia o script antes de aplicar.
3. **Aplicar GPOs** preserva o artefato compilado e executa o script com a sua identidade. **Domain Computers na raiz é alcance amplo, não piloto**: exige reconhecimento explícito e já fica elegível na atualização normal de GPO, antes das ondas. Escolha OU/grupo dedicado para limitar o piloto. Qualquer GPO `IpsecStudio-*` pré-existente bloqueia a aplicação: reverta antes.
4. **Atualizar política nos hosts** (sempre lê a política atual do AD; não reaplica a revisão aberta): purge do ticket de máquina, `gpupdate` e limpeza de SAs via WinRM.
5. **Verificar política efetiva**: compara com a revisão aberta (inclusive uma anterior), conferindo presença, habilitação, filtros, modos, autorização, criptografia, origem e status com o esperado. Leitura ausente/incompleta não é saudável. Num host Sem IPsec, espera-se ausência de regras do Studio.
6. **Sondar tráfego**: dos hosts gerenciados para os destinos do plano e os externos declarados (não todo o AD fora do piloto), conexão TCP e SA de quick mode, comparadas à simulação (esperado × observado). Depois de aplicar, espere cerca de 2 minutos antes de sondar. Um par que caiu para claro durante a implantação pode continuar em claro por alguns minutos com um host de fronteira, que só *solicita* IPsec. A simulação descreve o estado estável.

**Vários DCs.** Aplicar, reverter e as ondas gravam no DC emulador PDC e aguardam a confirmação dos DCs alcançáveis (padrão de até 3 minutos). O limite pode terminar com DCs pendentes: não é garantia de que todos já receberam a mudança. As ondas não são liberadas com pendências. Ao repetir a primeira onda após resolver uma pendência de GPO, o Studio refaz a leitura dos DCs, sem reaplicar as GPOs. Com um DC desligado, atualizar, verificar e sondar continuam funcionando pelos demais, desde que os membros usem mais de um DC como DNS. Criar e remover GPOs exige o PDC.
7. **Reverter** remove apenas as GPOs da revisão aplicada/tentada, mesmo que uma edição mais nova tenha sido salva. **Abrir revisão … para recuperação** permite acessar também as ondas antigas. Veja o [guia de recuperação](recuperacao.md).

O registro de execuções guarda cada ação, identidade, versão e saída.

## Operação do dia a dia

- **Colocar uma máquina em quarentena**: adicione-a ao grupo de exclusão e execute *Atualizar política nos hosts*. Nenhuma GPO muda.
- **Dar acesso a um servidor restrito**: adicione o computador ao grupo de acesso; renove o ticket de máquina (Atualizar) ou aguarde o reinício.
- **Nova versão do plano**: salve a edição sem perder o alvo de recuperação. Reverta/reconcilie ondas anteriores em ordem inversa e reverta as GPOs da revisão aplicada; só então compile/aplique a nova.

## Ondas, aprovação e grupos novos (0.4)

- **Grupos novos** (02 · Perfis → Grupos novos): declare o nome e, em 04 · Implantar, use *Preparar grupos no AD* (exige nota de aprovação). Depois releia o inventário para usá-los nos perfis.
- **Ondas** (02 · Perfis → Ondas): cada onda lista computadores por grupo de perfil, em ordem. Em 03 · Simular, o seletor *Após a onda N* mostra a matriz com a associação daquele ponto.
- **Aplicar onda** (04 · Implantar): exige esta revisão das GPOs aplicada e ondas anteriores aprovadas; grava associações, atualiza/verifica os hosts da onda, aguarda estabilização e sonda também iniciadores que podem ser recusados na entrada. Resultados vazios, incompletos, inconclusivos ou DCs pendentes não liberam expansão. Reverter uma onda anterior não aprova a seguinte: reaplique e valide na ordem.
- **Reverter onda** desfaz somente associações que o diário registrou como acrescentadas por ela, da última onda para a primeira. Ainda pode ser feito após remover as GPOs. Estados interrompidos e diários ausentes exigem reconciliação manual; não se presume propriedade de membros anteriores.
- **Sonda negativa**: TCP não conectar não prova sozinho que o IPsec causou o bloqueio. Sem uma conexão positiva para o mesmo destino na coleta, o resultado negativo fica inconclusivo. A decisão da onda exige também política efetiva correspondente e cobertura não vazia dos pares esperados. A verificação confere o nome da máquina que respondeu; endereço duplicado ou herança de OU desconhecida não gera previsão conclusiva.
- **Nota de aprovação**: obrigatória para aplicar GPOs, preparar grupos e aplicar ondas; fica no registro de execuções junto com a identidade Windows.
