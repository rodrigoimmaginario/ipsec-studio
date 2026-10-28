// One-off patch for CV4.DS1 (waves, new groups). Safe to delete after use.
import fs from 'node:fs';
const read = (f) => fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n');
const rep = (s, a, b) => { if (!s.includes(a)) throw new Error('missing: ' + a.slice(0, 70)); return s.replace(a, b); };

let s = read('app/IpsecStudio/PlanRules.cs');
s = rep(s, '    public static bool ValidGroupName(string name) => GroupNamePattern().IsMatch(name);',
`    public static bool ValidGroupName(string name) => GroupNamePattern().IsMatch(name);
    [GeneratedRegex(@"\\A((CN|OU)=[^,=]+,)+(DC=[^,=]+,)*DC=[^,=]+\\z", RegexOptions.IgnoreCase)] private static partial Regex ContainerDnPattern();
    [GeneratedRegex(@"\\A[\\p{L}\\p{N} ._()-]{0,200}\\z")] private static partial Regex DescriptionPattern();
    public static bool ValidContainerDn(string dn) => ContainerDnPattern().IsMatch(dn);
    // Membership after the first 'count' waves: groups gain the wave members. Used by simulation, probes and tests.
    public static InventorySnapshot AfterWaves(PlanInput plan, InventorySnapshot snapshot, int count) {
        var extra = new Dictionary<string, HashSet<string>>();
        foreach (var w in plan.Waves.Take(Math.Max(0, count))) foreach (var m in w.Members) {
            if (!extra.TryGetValue(m.GroupId, out var set)) extra[m.GroupId] = set = [];
            set.UnionWith(m.ComputerIds);
        }
        if (extra.Count == 0) return snapshot;
        return snapshot with { Objects = snapshot.Objects.Select(o => extra.TryGetValue(o.Id, out var add) ? o with { MemberIds = o.MemberIds.Union(add).ToArray() } : o).ToArray() };
    }`);
s = rep(s, 'input.Settings.Crypto is null || input.Settings.SecureSubnets is null) throw new ValidationException(L.T("plan.incomplete"));',
`input.Settings.Crypto is null || input.Settings.SecureSubnets is null || input.NewGroups is null || input.Waves is null) throw new ValidationException(L.T("plan.incomplete"));
        Require(input.NewGroups.Length <= 100 && input.Waves.Length <= 50, L.T("plan.limit"));`);
s = rep(s, '        var dcAddresses = snapshot.Objects.Where(o => o.Kind == "dc"',
`        if (input.Settings.GroupContainer is not null) Require(ValidContainerDn(input.Settings.GroupContainer), L.T("groups.container"));
        var newGroupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in input.NewGroups) {
            if (g is null) throw new ValidationException(L.T("plan.incomplete"));
            Require(Guid.TryParseExact(g.Id, "D", out _) && ValidGroupName(g.Name) && DescriptionPattern().IsMatch(g.Description ?? ""), L.T("groups.invalid", g.Name));
            Require(newGroupNames.Add(g.Name) && !snapshot.Objects.Any(o => o.Kind == "group" && o.Name.Equals(g.Name, StringComparison.OrdinalIgnoreCase)), L.T("groups.exists", g.Name));
        }
        var profileGroupIds = new HashSet<string>(input.Restricted.Where(r => r.Included).SelectMany(r => new[] { r.ProtectedGroupId, r.AccessGroupId }).Where(x => x is not null).Select(x => x!));
        foreach (var id in new[] { input.BaseGroupId, input.NoPolicyGroupId, input.BoundaryGroupId, input.ExclusionGroupId, input.Settings.ManagementGroupId }) if (id is not null) profileGroupIds.Add(id);
        var waveIds = new HashSet<string>();
        foreach (var w in input.Waves) {
            if (w is null || w.Members is null) throw new ValidationException(L.T("plan.incomplete"));
            Require(Guid.TryParseExact(w.Id, "D", out _) && waveIds.Add(w.Id), L.T("waves.id"));
            Text(w.Name, 100, L.T("waves.name"));
            foreach (var m in w.Members) {
                Require(m is not null && profileGroupIds.Contains(m.GroupId), L.T("waves.group", w.Name));
                Require(m!.ComputerIds is { Length: > 0 and <= 10000 } && m.ComputerIds.All(c => map.TryGetValue(c, out var o) && IsComputer(o) && o.Kind != "dc" && ValidSid(o.Sid)), L.T("waves.computer", w.Name));
            }
            warnings.Add(L.T("waves.pending", w.Name, w.Members.Sum(m => m.ComputerIds.Length)));
        }
        var dcAddresses = snapshot.Objects.Where(o => o.Kind == "dc"`);
fs.writeFileSync('app/IpsecStudio/PlanRules.cs', s);

let l = read('app/IpsecStudio/L10n.cs');
l = rep(l, '        // simulator', `        ["groups.container"] = ("Contêiner de grupos deve ser um DN de CN/OU do domínio.", "Group container must be a CN/OU distinguished name in the domain."),
        ["groups.invalid"] = ("Grupo novo inválido: {0}", "Invalid new group: {0}"),
        ["groups.exists"] = ("Grupo já existe ou nome repetido: {0}", "Group already exists or name repeated: {0}"),
        ["waves.id"] = ("Identificador de onda inválido ou repetido.", "Invalid or duplicate wave identifier."),
        ["waves.name"] = ("Nome da onda", "Wave name"),
        ["waves.group"] = ("{0}: grupo da onda não é um grupo de perfil deste plano.", "{0}: wave group is not a profile group of this plan."),
        ["waves.computer"] = ("{0}: computador da onda inválido (deve existir no inventário, ter SID e não ser DC).", "{0}: invalid wave computer (must exist in the inventory, have a SID and not be a DC)."),
        ["waves.pending"] = ("Onda {0}: {1} associação(ões) ainda não aplicadas; a simulação por onda mostra o efeito.", "Wave {0}: {1} membership(s) not applied yet; per-wave simulation shows the effect."),
        ["waves.notFound"] = ("Onda não encontrada no plano.", "Wave not found in the plan."),
        ["waves.blocked"] = ("Onda anterior ({0}) falhou e não foi revertida; expansão bloqueada.", "Previous wave ({0}) failed and was not rolled back; expansion is blocked."),
        ["deploy.approval"] = ("Nota de aprovação obrigatória.", "An approval note is required."),
        ["deploy.noGroups"] = ("Nenhum grupo novo declarado.", "No new group declared."),
        // simulator`);
fs.writeFileSync('app/IpsecStudio/L10n.cs', l);

let c = read('app/IpsecStudio/Compiler.cs');
c = rep(c, '    const string Preamble = """', `    // Wave: membership changes only. Everything is addressed by SID; names never reach PowerShell.
    public static WaveCompilation CompileWave(SavedPlan plan, string waveId) {
        var a = PlanRules.Assess(plan.Input, plan.Inventory);
        var wave = plan.Input.Waves.FirstOrDefault(w => w.Id == waveId) ?? throw new ValidationException(L.T("waves.notFound"));
        var map = plan.Inventory.Objects.ToDictionary(o => o.Id);
        var lines = new List<string>(); var undo = new List<string>(); var computers = new List<string>();
        foreach (var m in wave.Members) {
            var gsid = map[m.GroupId].Sid!;
            foreach (var cid in m.ComputerIds) {
                var csid = map[cid].Sid!; computers.Add(cid);
                lines.Add($"if(-not (Get-ADGroupMember -Identity '{gsid}' | Where-Object {{ $_.SID.Value -eq '{csid}' }})){{Add-ADGroupMember -Identity '{gsid}' -Members '{csid}'; $changed++}}");
                undo.Add($"if(Get-ADGroupMember -Identity '{gsid}' | Where-Object {{ $_.SID.Value -eq '{csid}' }}){{Remove-ADGroupMember -Identity '{gsid}' -Members '{csid}' -Confirm:$false; $changed++}}");
            }
        }
        var head = MembershipPreamble.Replace("__DOMAINSID__", plan.Inventory.DomainSid).Replace("__MARKER__", $"IpsecStudio plan {plan.Id} v{plan.Version} wave {wave.Id}");
        var apply = head + NL + string.Join(NL, lines) + NL + "@{marker=$marker;changed=$changed}|ConvertTo-Json -Compress";
        var rollback = head + NL + string.Join(NL, undo) + NL + "@{marker=$marker;changed=$changed}|ConvertTo-Json -Compress";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apply + "\\n--\\n" + rollback))).ToLowerInvariant();
        return new(wave.Id, wave.Name, computers.Distinct().ToArray(), apply, rollback, hash);
    }
    // New security groups, created before they can be referenced (they need a SID in the inventory).
    public static GroupsCompilation CompileGroups(SavedPlan plan) {
        PlanRules.Assess(plan.Input, plan.Inventory);
        if (plan.Input.NewGroups.Length == 0) throw new ValidationException(L.T("deploy.noGroups"));
        var container = plan.Input.Settings.GroupContainer ?? "CN=Users," + plan.Inventory.DomainDn;
        if (!PlanRules.ValidContainerDn(container)) throw new ValidationException(L.T("groups.container"));
        var lines = plan.Input.NewGroups.Select(g => $"if(-not (Get-ADGroup -Filter \\"Name -eq '{g.Name}'\\")){{New-ADGroup -Name '{g.Name}' -GroupScope Global -GroupCategory Security -Path '{container}' -Description '{g.Description}'; $changed++}}");
        var head = MembershipPreamble.Replace("__DOMAINSID__", plan.Inventory.DomainSid).Replace("__MARKER__", $"IpsecStudio plan {plan.Id} v{plan.Version} groups");
        var apply = head + NL + string.Join(NL, lines) + NL + "@{marker=$marker;changed=$changed}|ConvertTo-Json -Compress";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apply))).ToLowerInvariant();
        return new(plan.Input.NewGroups.Select(g => g.Name).ToArray(), container, apply, hash);
    }
    const string MembershipPreamble = """
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
Import-Module ActiveDirectory
$domain=Get-ADDomain
if($domain.DomainSID.Value -ne '__DOMAINSID__'){throw "Wrong domain: $($domain.DNSRoot)"}
$marker='__MARKER__'
$changed=0
""";
    const string Preamble = """`);
fs.writeFileSync('app/IpsecStudio/Compiler.cs', c);
console.log('patched');
