using System.Text.Json;
using IpsecStudio;
using Xunit;

namespace IpsecStudio.Tests;

public class PersistenceAndDeploymentTests : IDisposable {
    readonly LabFixture f = new();
    readonly string folder = Path.Combine(Path.GetTempPath(), "ipsec-tests-" + LabFixture.Id());
    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

    [Fact]
    public async Task Schema2_round_trip_versions_deployment_log_and_legacy_documents_ignored() {
        var repo = new PlanRepository(folder);
        var saved = await repo.Save(null, f.Plan, f.Snapshot, PlanRules.Assess(f.Plan, f.Snapshot), "tester");
        var again = (await new PlanRepository(folder).Get(saved.Id))!;
        Assert.Equal(2, again.SchemaVersion); Assert.Equal(2, again.Input.Restricted.Length); Assert.Null(again.Input.ExpectedVersion);
        await Assert.ThrowsAsync<ConflictException>(() => repo.Save(saved.Id, f.Plan with { ExpectedVersion = 0 }, f.Snapshot, again.Assessment, "tester"));
        await Assert.ThrowsAsync<ValidationException>(() => repo.Save(null, f.Plan with { ExpectedVersion = 1 }, f.Snapshot, again.Assessment, "tester"));
        var v2 = await repo.Save(saved.Id, f.Plan with { ExpectedVersion = 1, Name = "Lab v2" }, f.Snapshot, again.Assessment, "tester");
        Assert.Equal(2, v2.Version); Assert.True(File.Exists(Path.Combine(folder, saved.Id, "000001.json")));
        await repo.AppendDeployment(new(DateTimeOffset.UtcNow, "tester", saved.Id, 2, "abc", "apply", true, "{}"));
        await repo.AppendDeployment(new(DateTimeOffset.UtcNow.AddSeconds(1), "tester", saved.Id, 2, "", "verify", false, "x"));
        var log = await repo.ListDeployments(saved.Id);
        Assert.Equal(2, log.Length); Assert.Equal("apply", log[0].Action); Assert.False(log[1].Succeeded);
        var legacyDir = Path.Combine(folder, LabFixture.Id()); Directory.CreateDirectory(legacyDir);
        await File.WriteAllTextAsync(Path.Combine(legacyDir, "000001.json"), "{\"id\":\"x\",\"input\":{\"segments\":[]}}");
        Assert.Single(await repo.List());
        Assert.Equal("DH14", JsonSerializer.Deserialize<SavedPlan>(JsonSerializer.Serialize(v2, Json.Settings), Json.Settings)!.Input.Settings.Crypto.KeyExchange);
        Assert.Equal(Compiler.Compile(v2).Sha256, Compiler.Compile((await new PlanRepository(folder).Get(saved.Id))!).Sha256);
        Assert.Null(await repo.Get(LabFixture.Id()));
        await Assert.ThrowsAsync<ValidationException>(() => repo.Get("../etc"));
    }

    [Theory]
    [InlineData(true, "AES256", null, "encrypted")]
    [InlineData(true, "None", null, "authenticated")]
    [InlineData(true, null, null, "clear")]
    [InlineData(false, null, null, "blocked")]
    [InlineData(false, null, "WinRM failed", "unreachable")]
    public void Probe_classification_matches_simulator_vocabulary(bool tcp, string? cipher, string? error, string expected) =>
        Assert.Equal(expected, Deployment.Classify(tcp, cipher, error));

    [Fact]
    public void Localization_falls_back_and_formats() {
        L.Lang = "pt";
        Assert.Equal("missing.key", L.T("missing.key"));
        Assert.Equal("Nome: texto obrigatório, até 5 caracteres, sem caracteres de controle.", L.T("text.invalid", "Nome", 5));
        L.Lang = "xx"; Assert.Equal("pt", L.Lang);
        L.Lang = "en"; Assert.StartsWith("Plan is required", L.T("plan.required"));
        L.Lang = "pt";
    }
}
