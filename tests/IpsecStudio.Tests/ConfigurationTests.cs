using System.Text.Json;
using Xunit;

namespace IpsecStudio.Tests;

public class ConfigurationTests {
    // The shipped appsettings.json must parse: a broken file aborts the process before any log line is written (lab finding, 2026-10-28).
    [Fact]
    public void Shipped_appsettings_is_valid_json_with_required_sections() {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(doc.RootElement.TryGetProperty("Directory", out _));
        Assert.True(doc.RootElement.TryGetProperty("Storage", out var storage));
        Assert.Contains('\\', storage.GetProperty("Root").GetString()!);
        Assert.True(doc.RootElement.TryGetProperty("Deployment", out _));
        Assert.Equal(5090, doc.RootElement.GetProperty("Port").GetInt32());
    }
}
