# Instalação e configuração

[English](../en/install.md)

## Onde executar

Numa estação ou servidor de administração **ingressado no domínio**, com RSAT instalado (`Get-WindowsCapability -Online -Name Rsat.*` ou *Features* no Windows Server). O Studio usa os módulos PowerShell `ActiveDirectory`, `GroupPolicy` e `NetSecurity`, e `powershell.exe` 5.1.

Execute-o **com a sua conta de administrador**, como faria com o GPMC. O processo herda essa identidade para ler o AD, gravar GPOs e abrir sessões WinRM nos hosts. Não há conta de serviço nem segredos armazenados.

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
| `Directory:ExpectedDomain` | Nome DNS do domínio; o inventário é recusado se vier de outro domínio |
| `Storage:Root` | Pasta dos planos versionados e do registro de execuções |
| `Deployment:Enabled` | `true` permite aplicar/reverter; `false` cria uma instância somente de leitura e planejamento |
| `Deployment:ProbePort` | Porta TCP usada pela sonda (padrão 5985, WinRM) |
| `Deployment:OutsideCheck` | Opcional, `IPv4:porta` fora da sub-rede segura que todo host verificado deve alcançar, por exemplo um servidor de atualização ou DNS externo. Se falhar, o host aparece como não saudável |

As mesmas chaves podem vir de variáveis de ambiente (`Deployment__Enabled`, `Storage__Root`, …).

## Executar

`scripts\Start-App.ps1` ou diretamente `IpsecStudio.exe`. Abra `http://127.0.0.1:<porta>/`. Logs em `C:\ProgramData\IpsecStudio\logs`.

## Preparar o domínio

Crie os grupos de segurança que representarão os perfis (por exemplo `IPSEC-SQL-Servers`, `IPSEC-SQL-Clients`, `IPSEC-Boundary`, `IPSEC-NoPolicy`, `IPSEC-Exclusion`, `IPSEC-Admin`). Inclua a estação de administração nos grupos de acesso dos servidores que ela precisará gerenciar pela rede, ou num grupo de gestão.

Garanta WinRM habilitado nos hosts (padrão no Windows Server) para as etapas *Atualizar*, *Verificar* e *Sondar*.
