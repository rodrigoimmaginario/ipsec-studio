# Changelog

## 0.5.0 — 2026-11-03

Reliability: revision-based recovery, owned memberships, scoped predictions, complete evidence and safer first use.

- Recovery targets an immutable applied/attempted revision, not the latest edit. New applies preserve the compiled artifact and verify its integrity on recovery.
- Wave ownership journals preserve preexisting and primary memberships; strict AD adds do not claim concurrent duplicates. Attempts are recorded before mutation and runner exceptions retain recovery evidence; interrupted mutations need manual reconciliation. Reverse order, complete evidence, current GPO checks and pending-DC gates precede expansion. Failures after writes are recorded; wave undo remains possible after GPO rollback/new edits.
- Shared authorization semantics for compiler/simulator; management, custom base, quarantine precedence and OU scope/inheritance fixes; missing inheritance evidence stays indeterminate. Unilateral management denials that may reuse a bidirectional SA are indeterminate and never approve expansion; ACLs are not broadened to hide mismatches. DC/RODC groups are explicitly denied Studio GPO application. Draft edits no longer modify the client-side saved snapshot; acknowledgements no longer invalidate compilation.
- Verification compares main/exempt rules, enablement, addresses, ports/protocol/profile/mode, auth/crypto references and sets, authorization, source and responding machine identity. Native CIDR and dotted-decimal masks are compared by network/prefix, including exemptions, without hiding changed scope. Incomplete scans/readings are not success. TCP failure alone is not attributed to IPsec.
- Default read-only setup, local domain confirmation and prerequisite diagnostics. Windows Negotiate authentication and operator SID checks protect the local API; storage ACLs protect plans/journals.
- Matrix is limited to 30 selected machines per query with indexed cells; inventory primary-group lookup is indexed. Probes stay within managed plan scope plus declared external targets; complete probing is still expensive.
- Group-container data is serialized as JSON; group permission resolution uses SIDs. Recovery guides and SECURITY.md in PT/EN; blocked browser storage no longer blanks the site.

**Validation:** 151 local Windows tests and real AD lab scenarios, with explicit corrections/resumed runs. The first DC failover sample had 68/72 matches; two later unchanged-build samples had 72/72. The original cause is not proven; this is not a seamless-availability SLA. A synthetic exception after an actual AD membership commit preserved pending ownership and refused unsafe retry/undo. No external pilot or production recommendation.

### PT-BR

- Recuperação da revisão aplicada ou tentada, com artefato preservado e integridade conferida; salvar outra versão não apaga a recuperação anterior.
- Ondas com diário durável de propriedade: preservam associações prévias e primárias; inclusão estrita não assume duplicatas concorrentes. Tentativas e falhas posteriores à escrita ficam registradas; reversão em ordem inversa.
- Autorização compartilhada entre compilador e simulador: gestão, base personalizada, quarentena e alcance/herança de OU. Herança desconhecida e negação unilateral da gestão sujeita a SA bidirecional permanecem indeterminadas, sem ampliar ACLs nem liberar expansão; DCs e RODCs não recebem GPO do Studio. Rascunhos e confirmações não alteram a revisão salva.
- Conferência da política efetiva, filtros, referências, cifra, origem e identidade do host. CIDR e máscara decimal nativos são comparados por rede/prefixo, sem ocultar mudança de alcance. Resultados incompletos ou negativos sem testemunho positivo não liberam expansão.
- Somente leitura por padrão, domínio confirmado e diagnósticos. Autenticação Windows e SID do operador protegem a API; ACLs protegem planos e diários.
- Matriz limitada a 30 máquinas por consulta, células indexadas e índice de grupo primário. Sondas limitadas ao plano e aos destinos externos declarados; a sondagem completa continua cara.
- Dados de criação de grupos serializados; permissões resolvidas por SID. Guias de recuperação e SECURITY.md bilíngues; site tolera armazenamento do navegador bloqueado e explicita limites de quarentena/ESP/piloto.

**Validação:** 151 testes locais Windows e cenários em AD real, com correções e retomadas explícitas. Primeiro failover 68/72, dois posteriores 72/72 no mesmo build de teste; causa inicial não comprovada, sem SLA de continuidade. Exceção sintética após gravação AD real preservou propriedade pending e recusou retry/undo inseguros. Sem piloto externo nem recomendação de produção.

## 0.4.0 — 2026-11-02

Operation: waves, identity, changes outside the Studio, on-the-wire evidence and several domain controllers.

- Waves: ordered membership additions to profile groups, written to AD by the Studio (SID only), with per-wave refresh/verify/probe and automatic stop of expansion on failure; per-wave simulation.
- Group preparation: new security groups created from the plan before being referenced.
- Approval note required for apply, group preparation and wave apply; stored in the execution log with the Windows identity.
- Identity propagation: per-host machine-ticket age versus the last profile-group change ("identity pending"), and per-host renew (ticket purge, gpupdate, SA cleanup).
- Changes outside the Studio: before apply or rollback, the `IpsecStudio-*` GPOs are compared with the compilation (rules, crypto sets, ICMP) and other GPOs with IPsec rules are found by an LDAP search on the IPsec client-side extension (313 GPOs in about 1.4 s instead of 31.4 s), with their reach in the plan. Any difference blocks the action until it is acknowledged; the acknowledgement is logged. A GPO with no active link is listed but does not block.
- On the wire: optional packet capture on the responders during the probe, counting ESP packets, clear connections accepted and clear attempts per pair; the capture is deleted from the host.
- Outbound: restricted and quarantine profiles use Require/Request. Outbound Require is only an explicit option, with a clear warning, because it cuts traffic to hosts without IPsec on the secure subnets. Optional outside-reachability check for verified hosts.
- Rollback refreshes the hosts; the Studio host goes first on apply and last on rollback.
- Several domain controllers: writes pinned to the PDC emulator, wait for GPO and group replication (configurable, 0–3600 s, default 180) and the pending DCs shown; refresh runs as a temporary SYSTEM scheduled task, one host at a time.
- Probe waits about 2 minutes after a change and retries before reporting a clear connection.
- The interface shows the version from the server instead of a fixed number.
- Script runner behind an interface; deployment orchestration covered by unit tests (coverage gate includes Deployment.cs). 105 tests.
- Lab regression (12 machines, 2 DCs, including one DC shut down): PASS.

Known limitations: Active Directory with several sites not tested; legacy IPsec policies (IP Security Policies) not detected; the reach of other GPOs ignores blocked inheritance, enforced links and security filtering; group changes made outside the Studio are not checked against the DC that issued the machine ticket; the package is not code-signed.

## 0.3.0 — 2026-10-27

- Five protection profiles as first-class plan entities (restricted servers, authenticated base, no IPsec, boundary, quarantine), plus optional management group.
- Simulator with Windows IPsec semantics (exemptions, modes, responder-side authorization, encryption) and initiator × responder matrix with explanations.
- Compiler to GPOs with connection security rules, crypto/authentication sets and IPsec transport authorization lists; deterministic scripts with SHA-256; security-group filtering with Deny Apply.
- Deployment with the operator's identity: apply, refresh (ticket purge, gpupdate, SA cleanup), verify (rule status, source GPO, authorization list), probe (expected × observed), rollback; append-only execution log.
- Bilingual interface and backend messages (pt-BR default, en), choice persisted.
- Validated end to end in a 7-host lab: 25/25 probe matches across quarantine and boundary configurations.

## 0.1.0 — 2026-10-26

- Read-only Active Directory inventory and versioned plan storage.
