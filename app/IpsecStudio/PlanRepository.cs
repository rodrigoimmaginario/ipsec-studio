using System.Text.Json;
namespace IpsecStudio;

public sealed class PlanRepository {
    private readonly string root;
    private readonly SemaphoreSlim gate = new(1, 1);
    public PlanRepository(string root) { this.root = Path.GetFullPath(root); Directory.CreateDirectory(this.root); LocalOperator.ProtectStorage(this.root); }
    private static string CheckId(string id) {
        if (!Guid.TryParseExact(id, "D", out var guid) || guid == Guid.Empty) throw new ValidationException(L.T("repo.badId"));
        return guid.ToString("D");
    }
    public async Task<SavedPlan?> Get(string id, int? version = null) {
        if (version is < 1 or > 999999) throw new ValidationException(L.T("repo.badId"));
        var folder = Path.Combine(root, CheckId(id));
        if (!Directory.Exists(folder)) return null;
        var file = version is null ? Directory.EnumerateFiles(folder, "*.json").OrderDescending(StringComparer.Ordinal).FirstOrDefault() : Path.Combine(folder, $"{version:D6}.json");
        if (file is null || !File.Exists(file)) return null;
        try { return JsonSerializer.Deserialize<SavedPlan>(await File.ReadAllTextAsync(file), Json.Settings) ?? throw new IOException("Corrupt plan file."); }
        catch (JsonException) { return null; } // schema-1 documents are ignored, never silently migrated
    }
    public async Task FreezeCompilation(Compilation compilation) {
        var dir=Path.Combine(root,CheckId(compilation.PlanId),"compiled"); Directory.CreateDirectory(dir);
        var file=Path.Combine(dir,CompilationKey(compilation.PlanVersion,compilation.Sha256));
        if(File.Exists(file)) { await ReadCompilation(compilation.PlanId,compilation.PlanVersion,compilation.Sha256); return; }
        var temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            await using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                await JsonSerializer.SerializeAsync(stream,compilation,Json.Settings); await stream.FlushAsync(); stream.Flush(true);
            }
            File.Move(temp,file,false);
        } finally { if(File.Exists(temp)) File.Delete(temp); }
    }
    static string CompilationKey(int version,string hash) {
        if(version is <1 or >999999 || hash.Length!=64 || hash.Any(c=>!Uri.IsHexDigit(c))) throw new ValidationException(L.T("repo.badId"));
        return $"{version:D6}-{hash.ToLowerInvariant()}.json";
    }
    public async Task<Compilation?> ReadCompilation(string id,int version,string hash) {
        var file=Path.Combine(root,CheckId(id),"compiled",CompilationKey(version,hash));
        if(!File.Exists(file)) return null;
        var c=JsonSerializer.Deserialize<Compilation>(await File.ReadAllTextAsync(file),Json.Settings) ?? throw new IOException("Corrupt compilation.");
        var actual=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(c.ApplyScript+"\n--\n"+c.RollbackScript))).ToLowerInvariant();
        if(c.PlanId!=id || c.PlanVersion!=version || actual!=hash || c.Sha256!=hash) throw new IOException("Compilation integrity check failed.");
        return c;
    }
    public async Task<Compilation?> AppliedCompilation(string id,int version) {
        // A failed apply can have left partial state; preserve its recovery artifact too. Only a successful rollback clears it.
        var last=(await ListDeployments(id)).Where(r=>r.Version==version).LastOrDefault(r=>r.Action=="apply" || r.Action=="rollback" && r.Succeeded);
        return last is {Action:"apply"} && last.Version==version && last.Hash.Length==64 ? await ReadCompilation(id,version,last.Hash) : null;
    }
    public string WaveJournalPath(string id, int version, string waveId) {
        if (version is < 1 or > 999999) throw new ValidationException(L.T("repo.badId"));
        return Path.Combine(root, CheckId(id), "waves", $"{version:D6}-{CheckId(waveId)}.json");
    }
    public async Task<IReadOnlyList<SavedPlan>> List() {
        var result = new List<SavedPlan>();
        foreach (var dir in Directory.EnumerateDirectories(root)) {
            if (!Guid.TryParseExact(Path.GetFileName(dir), "D", out _)) continue;
            var plan = await Get(Path.GetFileName(dir));
            if (plan is not null) result.Add(plan);
        }
        return result.OrderByDescending(p => p.UpdatedAt).ToArray();
    }
    public async Task<InventorySnapshot?> FindSnapshot(string id) {
        CheckId(id);
        foreach(var dir in Directory.EnumerateDirectories(root)) {
            var planId=Path.GetFileName(dir); if(!Guid.TryParseExact(planId,"D",out _)) continue;
            foreach(var file in Directory.EnumerateFiles(dir,"*.json").OrderDescending(StringComparer.Ordinal)) {
                if(!int.TryParse(Path.GetFileNameWithoutExtension(file),out var version) || version is <1 or >999999) continue;
                var plan=await Get(planId,version);
                if(plan?.Inventory.Id==id) return plan.Inventory;
            }
        }
        return null;
    }
    // Append-only execution log per plan. Records are evidence of what was run, never a claim that policy is effective.
    public async Task AppendDeployment(DeploymentRecord record) {
        var dir = Path.Combine(root, CheckId(record.PlanId), "deployments"); Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{record.Utc:yyyyMMddTHHmmssfffffff}Z-{record.Action}-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(record, Json.Settings));
    }
    public async Task<DeploymentRecord[]> ListDeployments(string id) {
        var dir = Path.Combine(root, CheckId(id), "deployments");
        if (!Directory.Exists(dir)) return [];
        var list = new List<DeploymentRecord>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.Ordinal)) list.Add(JsonSerializer.Deserialize<DeploymentRecord>(await File.ReadAllTextAsync(file), Json.Settings)!);
        return list.ToArray();
    }
    public async Task<SavedPlan> Save(string? id, PlanInput input, InventorySnapshot snapshot, PlanAssessment assessment, string identity) {
        await gate.WaitAsync();
        try {
            SavedPlan? old = null;
            if (id is not null) { old = await Get(id) ?? throw new ValidationException(L.T("api.planNotFound")); if (input.ExpectedVersion != old.Version) throw new ConflictException(L.T("repo.stale")); }
            else if (input.ExpectedVersion is not null) throw new ValidationException(L.T("repo.newWithVersion"));
            var key = id is null ? Guid.NewGuid().ToString("D") : CheckId(id);
            var version = (old?.Version ?? 0) + 1;
            if (version > 999999) throw new ValidationException(L.T("repo.versionLimit"));
            var now = DateTimeOffset.UtcNow;
            var saved = new SavedPlan(key, version, input.Name.Trim(), old?.CreatedAt ?? now, now, identity, input with { ExpectedVersion = null }, snapshot, assessment);
            var dir = Path.Combine(root, key); Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"{version:D6}.json");
            var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                // Flush before publishing. Versions are immutable; a failed write never replaces an existing version.
                await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    await JsonSerializer.SerializeAsync(stream, saved, Json.Settings);
                    await stream.FlushAsync(); stream.Flush(flushToDisk: true);
                }
                File.Move(temp, file, overwrite: false);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
            return saved;
        } finally { gate.Release(); }
    }
}
