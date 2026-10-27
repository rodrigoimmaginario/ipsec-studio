using System.Text.Json;
namespace IpsecStudio;

public sealed class PlanRepository {
    private readonly string root;
    private readonly SemaphoreSlim gate = new(1, 1);
    public PlanRepository(string root) { this.root = Path.GetFullPath(root); Directory.CreateDirectory(this.root); }
    private static string CheckId(string id) {
        if (!Guid.TryParseExact(id, "D", out var guid) || guid == Guid.Empty) throw new ValidationException(L.T("repo.badId"));
        return guid.ToString("D");
    }
    public async Task<SavedPlan?> Get(string id) {
        var folder = Path.Combine(root, CheckId(id));
        if (!Directory.Exists(folder)) return null;
        var file = Directory.EnumerateFiles(folder, "*.json").OrderDescending(StringComparer.Ordinal).FirstOrDefault();
        if (file is null) return null;
        try { return JsonSerializer.Deserialize<SavedPlan>(await File.ReadAllTextAsync(file), Json.Settings) ?? throw new IOException("Corrupt plan file."); }
        catch (JsonException) { return null; } // schema-1 documents are ignored, never silently migrated
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
    public async Task<InventorySnapshot?> FindSnapshot(string id) => (await List()).FirstOrDefault(p => p.Inventory.Id == id)?.Inventory;
    // Append-only execution log per plan. Records are evidence of what was run, never a claim that policy is effective.
    public async Task AppendDeployment(DeploymentRecord record) {
        var dir = Path.Combine(root, CheckId(record.PlanId), "deployments"); Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{record.Utc:yyyyMMddTHHmmssfff}Z-{record.Action}.json");
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
