# Guia de TDD

[English](../en/tdd.md)

Toda mudança de comportamento começa por um teste que falha. Esta página mostra onde fica o teste de cada camada, um exemplo real escrito antes do código, e como rodar tudo.

## Rotina

```powershell
scripts\run-checks.ps1      # testes xUnit + cobertura (gate ≥ 85 % em PlanRules, Simulator, Compiler) · smoke de UI · build
scripts\install-hooks.ps1   # uma vez por clone: o hook pre-push executa run-checks antes de qualquer push
```

Ciclo: **vermelho** (teste novo falha) → **verde** (menor código que passa) → **refatorar** (testes continuam verdes). O CI (`.github/workflows/checks.yml`) repete `run-checks.ps1` em pull requests para `main` e em tags `v*`, num runner Linux, cancelando execuções anteriores e com limite de 10 minutos.

## Onde fica cada teste

| Camada | Arquivo | O que cobre |
|---|---|---|
| Regras do plano | `tests/IpsecStudio.Tests/PlanRulesTests.cs` | validação, resolução de grupos aninhados, perfil efetivo por computador, conflitos, avisos, idioma |
| Simulador | `tests/IpsecStudio.Tests/SimulatorTests.cs` | resultado por par iniciador → destino: claro, autenticado, cifrado, bloqueado |
| Compilador | `tests/IpsecStudio.Tests/CompilerTests.cs` | GPOs geradas, filtragem Deny Apply, SDDL de autorização, determinismo do hash, só IPsec nativo |
| Persistência e implantação | `tests/IpsecStudio.Tests/PersistenceAndDeploymentTests.cs` | versões imutáveis, concorrência otimista, registro de execuções, classificação da sonda, localização |
| Interface | `tests/browser/prototype-render.mjs` | renderização das telas com API simulada, troca PT/EN persistida |

A fixture `LabFixture` reproduz o laboratório de referência (DC, base, dois servidores restritos, gestão, fronteira, sem política, host externo). Use-a em vez de montar objetos à mão; derive variações com `with` e `f.With(objeto)`.

## Exemplo por camada — escrito antes do código

**Regra.** Quero que um computador em dois grupos restritos seja conflito:

```csharp
[Fact]
public void Computer_in_two_restricted_groups_is_a_conflict() {
    var s = f.With(f.FileServers with { MemberIds = [f.Fs1.Id, f.Db1.Id] });
    Assert.Single(PlanRules.Assess(f.Plan, s).Hosts.Single(h => h.ComputerId == f.Db1.Id).Conflicts);
}
```

**Simulador.** Quero que a lista de autorização seja decidida por quem recebe:

```csharp
[Fact]
public void Restricted_server_can_initiate_to_any_domain_computer() =>
    Assert.Equal("encrypted", f.Outcome(f.Db1.Id, f.ClientNo.Id));
```

**Compilador.** Quero que a quarentena vença qualquer outro perfil:

```csharp
[Fact]
public void Every_non_quarantine_gpo_denies_the_exclusion_group() {
    var c = Compiler.Compile(f.Saved());
    Assert.All(c.Gpos.Where(g => g.Profile != "quarantine"), g => Assert.Contains(f.Exclusion.Sid!, g.DenyGroupSids));
}
```

**Persistência.** Quero que salvar com versão antiga seja recusado:

```csharp
await Assert.ThrowsAsync<ConflictException>(() => repo.Save(saved.Id, f.Plan with { ExpectedVersion = 0 }, f.Snapshot, a, "tester"));
```

**Interface.** Quero que a troca para inglês seja lembrada:

```js
vm.runInContext("setLang('en'); render()", context);
assert.equal(storage.get('ipsec-studio-lang'), 'en');
```

## Quando o teste de unidade não basta

Mudanças em `Simulator.cs`, `Compiler.cs` ou `Deployment.cs` alteram o que o Windows fará. O teste de unidade garante o contrato previsto; o laboratório garante que o Windows concorda. Rode o ciclo completo (aplicar → atualizar → verificar → sondar → reverter) e aceite só com a sonda 100 % coincidente com a simulação.

## Cobertura

`scripts/coverage.ps1` exige ao menos 85% das linhas C# de `PlanRules`, `Simulator`, `Compiler` e `Deployment`. **Cobertura C# não mede execução das strings PowerShell.** `Program.cs` e o coletor de AD precisam também de testes reais.

No Windows, o gate executa PowerShell com AD simulado para testar propriedade/interrupção de ondas e analisar a sintaxe dos roteiros; testa ainda a API real em armazenamento isolado (anônimo 401, operador 200, POST sem token 403) e o fluxo no Edge com API de exemplo (salvar/aplicar v1, editar/salvar v2, reverter v1). Esses testes não alteram o AD. É necessário Edge instalado; `EDGE` permite informar outro caminho.

O CI Linux não executa esses testes Windows; os testes específicos aparecem como ignorados. A regressão em AD real e o aceite do operador continuam obrigatórios. A rotina privada exige aprovação explícita de custo antes de ligar Azure e desaloca as VMs ao terminar.
