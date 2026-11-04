using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json.Serialization;
using IpsecStudio;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Negotiate;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This release requires Windows with RSAT AD/GPO PowerShell.");
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Configuration.AddJsonFile(DirectoryReader.SettingsPath(builder.Configuration), optional:true, reloadOnChange:true).AddEnvironmentVariables().AddCommandLine(args);
var port = builder.Configuration.GetValue("Port", 5080);
if (port is < 1024 or > 65535) throw new InvalidOperationException("Invalid port.");
builder.WebHost.ConfigureKestrel(options => { options.Listen(IPAddress.Loopback, port); options.Limits.MaxRequestBodySize = 2_097_152; });
builder.Services.ConfigureHttpJsonOptions(options => { options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow; });
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
builder.Services.AddAuthorization();
builder.Services.AddSingleton<DirectoryReader>();
builder.Services.AddSingleton(new PlanRepository(builder.Configuration["Storage:Root"] ?? throw new InvalidOperationException("Storage path required.")));
builder.Services.AddSingleton<IScriptRunner, PowerShellRunner>();
builder.Services.AddSingleton<Deployment>();
var app = builder.Build();
const string Version = "0.5.1";
var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var identity = WindowsIdentity.GetCurrent().Name;
var operatorSid = WindowsIdentity.GetCurrent().User!.Value;
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context,next) => {
    if (context.User.Identity?.IsAuthenticated != true) { await context.ChallengeAsync(); return; }
    if (!LocalOperator.Allowed(context.User,operatorSid)) { context.Response.StatusCode=403; return; }
    await next(context);
});
var origin = $"http://127.0.0.1:{port}";
app.Use(async (context, next) => {
    var requested = context.Request.Headers["X-Lang"].ToString();
    L.Lang = requested.Length > 0 ? requested : (context.Request.Headers.AcceptLanguage.ToString().StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "pt");
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; connect-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
    context.Response.Headers["Cache-Control"] = "no-store";
    // Local tool for the logged-on administrator, like GPMC. Not a multi-user network service.
    if (context.Request.Host.Host is not ("127.0.0.1" or "localhost") || context.Request.Host.Port != port) { context.Response.StatusCode = 400; return; }
    if (context.Request.Path.StartsWithSegments("/api") && HttpMethods.IsPost(context.Request.Method)) {
        var incomingOrigin = context.Request.Headers.Origin.ToString();
        if ((incomingOrigin.Length > 0 && incomingOrigin != origin && incomingOrigin != $"http://localhost:{port}") || context.Request.Headers["X-Plan-Token"] != token) {
            context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { error = L.T("api.badOrigin") }); return;
        }
    }
    try { await next(context); }
    catch (Exception ex) {
        var code = ex switch { ValidationException => 400, ConflictException => 409, DirectoryUnavailableException => 503, UnauthorizedAccessException => 403, BadHttpRequestException b => b.StatusCode, System.Text.Json.JsonException => 400, InvalidOperationException => 502, _ => 500 };
        app.Logger.Log(code == 500 ? LogLevel.Error : LogLevel.Warning, ex, "Request failed {Trace}", context.TraceIdentifier);
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = code;
        var message = ex is ValidationException or ConflictException or DirectoryUnavailableException or UnauthorizedAccessException or InvalidOperationException ? ex.Message : code == 400 ? L.T("api.badRequest") : L.T("api.failed");
        await context.Response.WriteAsJsonAsync(new { error = message, traceId = context.TraceIdentifier });
    }
});
app.UseDefaultFiles(); app.UseStaticFiles();
app.MapGet("/api/status", (DirectoryReader directory, Deployment deployment) => new { version = Version, identity, host = Environment.MachineName, directoryServer = directory.Server, domain = directory.Domain, suggestedDomain = directory.SuggestedDomain, token, inventory = directory.Current, lastReadError = directory.LastError, deploymentEnabled = deployment.Enabled, probePort = deployment.ProbePort });
app.MapPost("/api/preflight", async (IScriptRunner runner) => { var (ok,output)=await runner.Run(Preflight.Script,TimeSpan.FromSeconds(45)); if(!ok) throw new InvalidOperationException(L.T("deploy.remoteFailed",output)); return Results.Content(output,"application/json"); });
app.MapPost("/api/directory/configure", async (DirectoryConfiguration request, DirectoryReader directory) => { await directory.Configure(request.Domain); return new { domain = directory.Domain }; });
app.MapPost("/api/inventory/refresh", async (DirectoryReader directory, HttpContext context) => Results.Ok(await directory.Refresh(context.RequestAborted)));
async Task<(InventorySnapshot Snapshot, PlanAssessment Assessment)> Evaluate(PlanInput input, DirectoryReader directory, PlanRepository repository) {
    var snapshot = directory.Current?.Id == input.InventoryId ? directory.Current : await repository.FindSnapshot(input.InventoryId);
    if (snapshot is null) throw new ValidationException(L.T("api.inventoryMissing"));
    var assessment = PlanRules.Assess(input, snapshot);
    if (directory.Current?.Id != snapshot.Id) assessment = assessment with { Warnings = [.. assessment.Warnings, L.T("api.storedSnapshot")] };
    return (snapshot, assessment);
}
app.MapPost("/api/plans/assess", async (PlanInput input, DirectoryReader directory, PlanRepository repository) => (await Evaluate(input, directory, repository)).Assessment);
app.MapPost("/api/plans/simulate", async (PlanInput input, DirectoryReader directory, PlanRepository repository, HttpContext context) => {
    var (snapshot, assessment) = await Evaluate(input, directory, repository);
    var waves = int.TryParse(context.Request.Query["wave"], out var n) ? Math.Clamp(n, 0, input.Waves?.Length ?? 0) : 0;
    var membership = PlanRules.AfterWaves(input, snapshot, waves);
    var waveAssessment = waves == 0 ? assessment : PlanRules.Assess(input, membership);
    var requested = context.Request.Query["computers"].ToString();
    var selected = requested.Length == 0 ? null : requested.Split(',', StringSplitOptions.RemoveEmptyEntries);
    var matrixIds = Simulator.MatrixIds(input, waveAssessment, selected);
    return new { assessment, wave = waves, matrix = Simulator.Matrix(input, membership, waveAssessment, matrixIds), matrixIds, matrixLimit = Simulator.MaxMatrixHosts, waveHosts = waveAssessment.Hosts };
});
app.MapGet("/api/plans", async (PlanRepository repository) => (await repository.List()).Select(p => new { p.Id, p.Version, p.Name, p.UpdatedAt, domain = p.Inventory.Domain, capturedAt = p.Inventory.CapturedAt }));
app.MapGet("/api/plans/{id}", async (string id, int? version, PlanRepository repository) => { var saved = await repository.Get(id, version); return saved is null ? Results.NotFound(new { error = L.T("api.planNotFound") }) : Results.Ok(saved); });
app.MapPost("/api/plans", async (PlanInput input, DirectoryReader directory, PlanRepository repository) => { var (snapshot, assessment) = await Evaluate(input, directory, repository); var saved = await repository.Save(null, input, snapshot, assessment, identity); return Results.Created($"/api/plans/{saved.Id}", saved); });
app.MapPost("/api/plans/{id}", async (string id, PlanInput input, DirectoryReader directory, PlanRepository repository) => { var (snapshot, assessment) = await Evaluate(input, directory, repository); return Results.Ok(await repository.Save(id, input, snapshot, assessment, identity)); });
app.MapGet("/api/plans/{id}/compile", async (string id, int? version, PlanRepository repository) => { var saved = await repository.Get(id, version) ?? throw new ValidationException(L.T("api.planNotFound")); return await repository.AppliedCompilation(id,saved.Version) ?? Compiler.Compile(saved); });
app.MapGet("/api/plans/{id}/compile-groups", async (string id, PlanRepository repository) => { var saved = await repository.Get(id) ?? throw new ValidationException(L.T("api.planNotFound")); return Compiler.CompileGroups(saved); });
app.MapGet("/api/plans/{id}/compile-wave/{waveId}", async (string id, string waveId, PlanRepository repository) => { var saved = await repository.Get(id) ?? throw new ValidationException(L.T("api.planNotFound")); return Compiler.CompileWave(saved, waveId, repository.WaveJournalPath(saved.Id, saved.Version, waveId)); });
app.MapGet("/api/plans/{id}/drift", async (string id, PlanRepository repository, Deployment deployment) => { var saved = await repository.Get(id) ?? throw new ValidationException(L.T("api.planNotFound")); return await deployment.Drift(saved.Id, saved.Version); });
app.MapPost("/api/wave", async (WaveRequest request, Deployment deployment) => await deployment.ApplyWave(request, identity));
app.MapGet("/api/plans/{id}/deployments", async (string id, PlanRepository repository) => await repository.ListDeployments(id));
app.MapPost("/api/deploy", async (DeployRequest request, Deployment deployment) => await deployment.Execute(request, identity));
app.MapPost("/api/hosts", async (HostRequest request, Deployment deployment) => await deployment.HostOperation(request, identity));
app.MapPost("/api/probe", async (HostRequest request, Deployment deployment) => await deployment.Probe(request, identity));
app.Logger.LogInformation("IPsec Studio {Version} listening on {Origin}; identity {Identity}; deploymentEnabled={Deployment}", Version, origin, identity, app.Configuration.GetValue<bool>("Deployment:Enabled"));
await app.RunAsync();

public sealed record DirectoryConfiguration(string Domain);
