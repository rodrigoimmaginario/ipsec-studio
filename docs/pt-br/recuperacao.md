# Recuperação e reversão

[English](../en/recovery.md)

## Antes do piloto

- Tenha console da VM/servidor ou outro acesso administrativo independente da política em teste. Não dependa somente de WinRM.
- Comece numa OU ou grupo dedicado. **Domain Computers na raiz não é piloto limitado:** a GPO fica elegível para todo o domínio na atualização normal, antes de qualquer onda.
- Preserve `Storage:Root`: revisões, artefatos compilados e diários de ondas fazem parte da recuperação. Não publique esses arquivos; contêm dados do seu domínio.
- Mantenha DCs isentos e confira DNS, Kerberos, SYSVOL e replicação. A isenção não garante disponibilidade se outra política ou a rede os bloquear.

## Com o Studio acessível

1. Confira o registro: uma aplicação que falhou pode ter deixado estado parcial.
2. Use **Abrir revisão … para recuperação**, quando houver edição mais nova. Reabra a última edição por *Planos salvos* se quiser voltar a editar; uma revisão antiga não sobrescreve a nova.
3. Reverta ondas **da última para a primeira**. Só associações registradas como acrescentadas por aquela onda são removidas; membros anteriores são preservados.
4. Use **Reverter (remover GPOs)**. O alvo é a revisão aplicada/tentada, não a última edição. Novas aplicações preservam também o roteiro compilado com seu hash.
5. Confira DCs pendentes, atualize os hosts e verifique a política efetiva. Remover a GPO do AD não a remove imediatamente do cache de todos os hosts.

Se já removeu as GPOs, ainda pode reabrir a revisão e reverter suas ondas. Uma nova aplicação é recusada enquanto ondas anteriores estiverem aplicadas ou inconclusivas.

## Sem acesso pelo Studio

1. Pelo console ou estação administrativa com RSAT, abra o GPMC no domínio correto. Confira nome, GUID, descrição `IpsecStudio plan <id> v<versão>` e destino do vínculo. Exporte um backup/relatório antes de alterar.
2. **Desabilite/remova o vínculo da GPO específica** no destino correto. Não remova indiscriminadamente todas as GPOs `IpsecStudio-*`: pode haver outra revisão/plano ou vínculos adicionais.
3. Aguarde/confira replicação e, pelo console dos hosts afetados, execute como administrador:

   ```powershell
   gpupdate.exe /target:computer /force /wait:120
   ```

4. Confira regras ativas e teste o serviço real. Se associações IPsec antigas persistirem, leia as SAs e remova somente as do par afetado. Faça isso pelo console: limpar a SA de administração pode interromper sua sessão.
5. Mudanças de grupo podem exigir renovar o ticket de máquina ou reiniciar o host. `klist -li 0x3e7 purge` afeta os tickets da sessão SYSTEM, inclusive serviços; não use sem avaliar o impacto.
6. Se não houver um caminho administrativo independente, restaure-o pelo mecanismo de console/recuperação da sua infraestrutura. O Studio não oferece um canal de emergência fora de banda.

## Diário ausente, interrompido ou de uma versão antiga

Diários ficam em `<Storage:Root>/<plano>/waves/`, separados por revisão e onda. Estados `pending`/`removing`, arquivo `.lock` remanescente ou diário ausente impedem a reversão automática segura.

- Encerre a execução concorrente, preserve os arquivos e confira associações no PDC e nos outros DCs.
- Não apague o diário nem transforme um estado inconclusivo em `added` por suposição: isso poderia retirar uma associação que o Studio não criou.
- Reconcilie manualmente com aprovação e evidência; se não conseguir estabelecer a propriedade, reverta somente as associações cuja origem foi comprovada.
- Ondas executadas pela 0.4.0 publicada não possuem esse diário. Não é possível reconstruir automaticamente quais membros já existiam. Use o registro e a conferência manual; não há migração destrutiva.
- Aplicações antigas sem artefato congelado usam o roteiro reconstruído da revisão e continuam exigindo a marca exata da GPO. Confira-o antes de executar; isso não equivale a recuperar os bytes do roteiro antigo.
