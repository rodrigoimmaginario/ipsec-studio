# Instalação e configuração

[English](../en/install.md)

## Onde executar

Numa estação ou servidor de administração **ingressado no domínio**, com RSAT atualizado (incluindo `Add-ADGroupMember -DisablePermissiveModify`, disponível nos módulos atualizados desde setembro de 2020; `Get-WindowsCapability -Online -Name Rsat.*` ou *Features* no Windows Server). O Studio usa os módulos PowerShell `ActiveDirectory`, `GroupPolicy` e `NetSecurity`, e `powershell.exe` 5.1.

Execute-o **com a sua conta de administrador** e abra a página com essa mesma identidade Windows (Negotiate; pode haver solicitação de login). Outra conta local não pode usar a autoridade do processo. Não execute como serviço compartilhado.

Execute-o com essa conta, como faria com o GPMC. O processo herda essa identidade para ler o AD, gravar GPOs e abrir sessões WinRM nos hosts. Não há conta de serviço nem segredos armazenados.

## Publicar

```powershell
dotnet publish app/IpsecStudio/IpsecStudio.csproj -c Release -r win-x64 --self-contained true -o artifacts/app
```

O resultado é autocontido (.NET 10); não exige instalar runtime na estação.

## Configurar (`appsettings.json`)

| Chave | Significado |
|---|---|
| `Port` | Porta local (somente `127.0.0.1`) |
| `Directory:Server` | Controlador de domínio consultado. Vazio = o nome do domínio, e o Windows escolhe um DC disponível (recomendado com vários DCs). Criar e remover GPOs continua exigindo o DC emulador PDC |
| `Directory:ExpectedDomain` | Vazio por padrão: a interface propõe o sufixo DNS e exige confirmação. O inventário de outro domínio é recusado |
| `Storage:Root` | Pasta dos planos versionados e do registro de execuções |
| `Deployment:Enabled` | Padrão `false`: somente leitura/planejamento. Edite para `true` e reinicie para permitir alterações no AD |
| `Deployment:ProbePort` | Porta TCP usada pela sonda (padrão 5985, WinRM) |
| `Deployment:SettleSeconds` | Espera interna antes da sonda de uma onda (padrão 120 s, 0–600). Zero é útil em testes simulados, não para comprovar estado estável real |
| `Deployment:OutsideCheck` | Opcional, `IPv4:porta` fora da sub-rede segura que todo host verificado deve alcançar, por exemplo um servidor de atualização ou DNS externo. Se falhar, o host aparece como não saudável |

As mesmas chaves podem vir de variáveis de ambiente (`Deployment__Enabled`, `Storage__Root`, …).

## Executar

`scripts\Start-App.ps1` ou diretamente `IpsecStudio.exe`. Abra `http://127.0.0.1:<porta>/`. Logs em `C:\ProgramData\IpsecStudio\logs`.

## Primeiro uso

1. Abra a página como o operador que iniciou o Studio. Se houver 401/403, confira essa identidade; não desative a autenticação nem publique a porta para contornar o problema.
2. Use **Conferir pré-requisitos**: ingresso no domínio e módulos RSAT. Isso não comprova permissões no AD/WinRM remoto.
3. Confira e confirme o domínio. A escolha é guardada em `<Storage:Root>/directory.json`; variáveis de ambiente têm precedência. Para trocar de domínio, confirme outro na interface.
4. Leia o inventário, planeje e simule sem habilitar implantação. Antes de habilitar escrita, prepare um piloto e leia [recuperação](recuperacao.md).

Logs em arquivo são produzidos pelo `Start-App.ps1`; execução direta escreve no console.

## Preparar o domínio

Crie os grupos de segurança que representarão os perfis (por exemplo `IPSEC-SQL-Servers`, `IPSEC-SQL-Clients`, `IPSEC-Boundary`, `IPSEC-NoPolicy`, `IPSEC-Exclusion`, `IPSEC-Admin`). Inclua a estação de administração nos grupos de acesso dos servidores que ela precisará gerenciar pela rede, ou num grupo de gestão.

Garanta WinRM habilitado nos hosts (padrão no Windows Server) para as etapas *Atualizar*, *Verificar* e *Sondar*.
