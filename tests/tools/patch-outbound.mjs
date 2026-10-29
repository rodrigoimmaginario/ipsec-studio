// One-off patch for CV4.DS6: no outbound Require by default; explicit option with alert. Safe to delete after use.
import fs from 'node:fs';
const read = (f) => fs.readFileSync(f, 'utf8').replace(/\r\n/g, '\n');
const edit = (f, pairs) => { let s = read(f); for (const [a, b] of pairs) { if (!s.includes(a)) throw new Error(f + ' missing: ' + a.slice(0, 70)); s = s.replace(a, () => b); } fs.writeFileSync(f, s); };
edit('app/IpsecStudio/Models.cs', [[
  `    public bool ExemptIcmp { get; init; } = true;`,
  `    public bool ExemptIcmp { get; init; } = true;
    // Outbound Require for restricted and quarantine. Off by default: in the lab it cut all traffic leaving the secure subnet.
    public bool RequireOutbound { get; init; }`]]);
edit('app/IpsecStudio/PlanRules.cs', [[
  `        if (input.BaseMode == "request") warnings.Add(L.T("base.requestWarning"));`,
  `        if (input.BaseMode == "request") warnings.Add(L.T("base.requestWarning"));
        var strict = new List<string>();
        if (input.BaseMode == "require") strict.Add(L.T("label.base"));
        if (input.Settings.RequireOutbound) {
            strict.AddRange(input.Restricted.Where(r => r.Included).Select(r => r.Name));
            if (input.ExclusionGroupId is not null) strict.Add(L.T("label.quarantine"));
        }
        if (strict.Count > 0) warnings.Insert(0, L.T("outbound.requireAlert", string.Join(", ", strict)));`],
 [`    public static string ModeText(string mode) => mode switch {`,
  `    // Windows modes (inbound, outbound) per profile; the compiler and the simulator share this single source.
    public static (string In, string Out) Modes(PlanInput input, string profile) => profile switch {
        "restricted" or "quarantine" => ("Require", input.Settings.RequireOutbound ? "Require" : "Request"),
        "boundary" => ("Request", "Request"),
        "base" or "management" => input.BaseMode switch { "require" => ("Require", "Require"), "request" => ("Request", "Request"), _ => ("Require", "Request") },
        _ => ("None", "None")
    };
    public static string ModeText(string mode) => mode switch {`]]);
edit('app/IpsecStudio/Compiler.cs', [
 [`        var (bIn, bOut) = input.BaseMode switch { "require" => ("Require", "Require"), "request" => ("Request", "Request"), _ => ("Require", "Request") };`,
  `        var (bIn, bOut) = PlanRules.Modes(input, "base");
        var (sIn, sOut) = PlanRules.Modes(input, "restricted");`],
 [`, [], "Require", "Require", r.Encryption, true, Sddl(allow, true)`, `, [], sIn, sOut, r.Encryption, true, Sddl(allow, true)`],
 [`exclusionSid!, [], "Require", "Require", false, true,`, `exclusionSid!, [], sIn, sOut, false, true,`]]);
edit('app/IpsecStudio/Simulator.cs', [[
  `    static (string Out, string In) Modes(PlanInput plan, Endpoint e) => e.Profile switch {
        "restricted" or "quarantine" => ("require", "require"),
        "boundary" => ("request", "request"),
        "base" or "management" => plan.BaseMode switch { "require" => ("require", "require"), "request" => ("request", "request"), _ => ("request", "require") },
        _ => ("none", "none")
    };`,
  `    static (string Out, string In) Modes(PlanInput plan, Endpoint e) {
        var (i, o) = PlanRules.Modes(plan, e.Profile);
        return (o.ToLowerInvariant(), i.ToLowerInvariant());
    }`]]);
edit('app/IpsecStudio/L10n.cs', [[
  `        ["base.requestWarning"]`,
  `        ["outbound.requireAlert"] = ("ALERTA — saída 'Require' em: {0}. Com saída 'Require', o Windows bloqueou no laboratório todo o tráfego desses computadores para fora da sub-rede segura (internet, DNS externo, atualizações, agente do Azure). Não recomendado: mantenha a saída em 'Request'; a proteção continua decidida na entrada de cada destino.", "ALERT — outbound 'Require' on: {0}. With outbound 'Require', Windows blocked in the lab all traffic from these computers leaving the secure subnet (internet, external DNS, updates, Azure agent). Not recommended: keep outbound at 'Request'; protection is still decided on each responder's inbound side."),
        ["deploy.outsideInvalid"] = ("Deployment:OutsideCheck deve ser IPv4:porta.", "Deployment:OutsideCheck must be IPv4:port."),
        ["base.requestWarning"]`]]);
console.log('patched');
