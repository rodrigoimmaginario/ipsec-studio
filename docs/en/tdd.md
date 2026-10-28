# TDD guide

[Português](../pt-br/tdd.md)

Every behavior change starts with a failing test. This page shows where each layer's tests live, a real example written before the code, and how to run everything.

## Routine

```powershell
scripts\run-checks.ps1      # xUnit tests + coverage (gate ≥ 85 % on PlanRules, Simulator, Compiler) · UI smoke · build
scripts\install-hooks.ps1   # once per clone: the pre-push hook runs run-checks before any push
```

Cycle: **red** (new test fails) → **green** (smallest code that passes) → **refactor** (tests stay green). CI (`.github/workflows/checks.yml`) repeats `run-checks.ps1` on pull requests to `main` and on `v*` tags, on a Linux runner, cancelling superseded runs, with a 10-minute limit.

## Where each test lives

| Layer | File | What it covers |
|---|---|---|
| Plan rules | `tests/IpsecStudio.Tests/PlanRulesTests.cs` | validation, nested group resolution, effective profile per computer, conflicts, warnings, language |
| Simulator | `tests/IpsecStudio.Tests/SimulatorTests.cs` | outcome per initiator → responder pair: clear, authenticated, encrypted, blocked |
| Compiler | `tests/IpsecStudio.Tests/CompilerTests.cs` | generated GPOs, Deny Apply filtering, authorization SDDL, hash determinism, native IPsec only |
| Persistence and deployment | `tests/IpsecStudio.Tests/PersistenceAndDeploymentTests.cs` | immutable versions, optimistic concurrency, execution log, probe classification, localization |
| Interface | `tests/browser/prototype-render.mjs` | screen rendering with a stubbed API, persisted PT/EN switch |

The `LabFixture` reproduces the reference lab (DC, base, two restricted servers, management, boundary, no policy, external host). Use it instead of building objects by hand; derive variations with `with` and `f.With(object)`.

## One example per layer — written before the code

**Rule.** A computer in two restricted groups must be a conflict:

```csharp
[Fact]
public void Computer_in_two_restricted_groups_is_a_conflict() {
    var s = f.With(f.FileServers with { MemberIds = [f.Fs1.Id, f.Db1.Id] });
    Assert.Single(PlanRules.Assess(f.Plan, s).Hosts.Single(h => h.ComputerId == f.Db1.Id).Conflicts);
}
```

**Simulator.** The authorization list is decided by the responder:

```csharp
[Fact]
public void Restricted_server_can_initiate_to_any_domain_computer() =>
    Assert.Equal("encrypted", f.Outcome(f.Db1.Id, f.ClientNo.Id));
```

**Compiler.** Quarantine must win over every other profile:

```csharp
[Fact]
public void Every_non_quarantine_gpo_denies_the_exclusion_group() {
    var c = Compiler.Compile(f.Saved());
    Assert.All(c.Gpos.Where(g => g.Profile != "quarantine"), g => Assert.Contains(f.Exclusion.Sid!, g.DenyGroupSids));
}
```

**Persistence.** Saving with a stale version must be refused:

```csharp
await Assert.ThrowsAsync<ConflictException>(() => repo.Save(saved.Id, f.Plan with { ExpectedVersion = 0 }, f.Snapshot, a, "tester"));
```

**Interface.** Switching to English must be remembered:

```js
vm.runInContext("setLang('en'); render()", context);
assert.equal(storage.get('ipsec-studio-lang'), 'en');
```

## When a unit test is not enough

Changes to `Simulator.cs`, `Compiler.cs` or `Deployment.cs` change what Windows will do. The unit test guarantees the predicted contract; the lab guarantees that Windows agrees. Run the full cycle (apply → refresh → verify → probe → roll back) and accept only with the probe matching the simulation 100 %.

## Coverage

`scripts/coverage.ps1` reads the Cobertura report and prints covered lines per file. `Program.cs`, `DirectoryReader.cs` and `Deployment.cs` depend on real AD, WinRM and PowerShell and are outside the gate; their assurance comes from the lab.
