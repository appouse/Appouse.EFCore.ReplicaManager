using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Appouse.EFCore.ReplicaManager.Tests;

[ApiController]
[Route("api/markers")]
public sealed class MarkersController(MarkerContext db) : ControllerBase
{
    [HttpGet("source")]
    public Task<string> Get() => Source(db);

    [HttpPost("source")]
    public Task<string> Post() => Source(db);

    [HttpGet("forced-write")]
    [UseMasterDb]
    public Task<string> ForcedWrite() => Source(db);

    [HttpPost("forced-read")]
    [UseReplicaDb]
    public Task<string> ForcedRead() => Source(db);

    internal static Task<string> Source(MarkerContext context)
        => context.Markers.OrderBy(m => m.Id).Select(m => m.Source).FirstAsync();
}

/// <summary>Every handler here inherits the controller-level <c>[UseReplicaDb]</c>.</summary>
[ApiController]
[Route("api/reporting")]
[UseReplicaDb]
public sealed class ReportingController(MarkerContext db) : ControllerBase
{
    [HttpPost("inherited")]
    public Task<string> Inherited() => MarkersController.Source(db);

    [HttpPost("overridden")]
    [UseMasterDb]
    public Task<string> Overridden() => MarkersController.Source(db);
}

public sealed class WebRoutingTests : IClassFixture<TwoDatabaseFixture>, IAsyncLifetime
{
    private readonly TwoDatabaseFixture _fx;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public WebRoutingTests(TwoDatabaseFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddControllers().AddApplicationPart(typeof(MarkersController).Assembly);
        builder.Services.AddDbTargetMvcFilter();

        builder.Services.AddEfCoreMasterReplica(options =>
        {
            options.MasterConnectionString = _fx.MasterConnectionString;
            options.ReplicaConnectionString = _fx.ReplicaConnectionString;
        });
        builder.Services.AddMasterReplicaDbContext<MarkerContext>((options, cs) => options.UseSqlite(cs));

        _app = builder.Build();

        _app.MapControllers();
        _app.MapGet("/minimal/get", (MarkerContext db) => MarkersController.Source(db));
        _app.MapPost("/minimal/post", (MarkerContext db) => MarkersController.Source(db));
        _app.MapGet("/minimal/forced-write", (MarkerContext db) => MarkersController.Source(db)).UseMasterDb();
        _app.MapPost("/minimal/forced-read", (MarkerContext db) => MarkersController.Source(db)).UseReplicaDb();

        var group = _app.MapGroup("/minimal/group").UseReplicaDb();
        group.MapPost("/inherited", (MarkerContext db) => MarkersController.Source(db));

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task<string> GetAsync(string url) => await _client.GetStringAsync(url);

    private async Task<string> PostAsync(string url)
    {
        var response = await _client.PostAsync(url, content: null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Controller_GET_goes_to_the_replica()
        => Assert.Equal("replica", await GetAsync("/api/markers/source"));

    [Fact]
    public async Task Controller_POST_goes_to_the_master()
        => Assert.Equal("master", await PostAsync("/api/markers/source"));

    [Fact]
    public async Task UseWriteDb_overrides_the_GET_convention()
        => Assert.Equal("master", await GetAsync("/api/markers/forced-write"));

    [Fact]
    public async Task UseReadDb_overrides_the_POST_convention()
        => Assert.Equal("replica", await PostAsync("/api/markers/forced-read"));

    [Fact]
    public async Task A_controller_level_attribute_applies_to_its_actions()
        => Assert.Equal("replica", await PostAsync("/api/reporting/inherited"));

    [Fact]
    public async Task An_action_level_attribute_beats_the_controller_level_one()
        => Assert.Equal("master", await PostAsync("/api/reporting/overridden"));

    /// <summary>
    /// This app wires only the MVC filter. A Minimal API endpoint that carries neither an
    /// attribute helper nor the middleware is therefore never pinned, and correctly falls back to
    /// <see cref="MasterReplicaOptions.DefaultTarget"/> - which defaults to the master. Add
    /// <c>app.UseDbTargetRouting()</c> to apply the verb convention to Minimal APIs as well; that
    /// configuration is covered by <see cref="MiddlewareRoutingTests"/>.
    /// </summary>
    [Fact]
    public async Task An_unrouted_minimal_api_endpoint_falls_back_to_the_default_target()
    {
        Assert.Equal("master", await GetAsync("/minimal/get"));
        Assert.Equal("master", await PostAsync("/minimal/post"));
    }

    [Fact]
    public async Task Minimal_api_UseWriteDb_overrides_the_convention()
        => Assert.Equal("master", await GetAsync("/minimal/forced-write"));

    [Fact]
    public async Task Minimal_api_UseReadDb_overrides_the_convention()
        => Assert.Equal("replica", await PostAsync("/minimal/forced-read"));

    [Fact]
    public async Task A_route_group_applies_its_target_to_every_endpoint()
        => Assert.Equal("replica", await PostAsync("/minimal/group/inherited"));

    [Fact]
    public async Task Concurrent_requests_of_mixed_verbs_stay_isolated()
    {
        // Warm the pipeline first: the very first request through TestServer pays for JIT and
        // routing-table construction, and that cold start - not the package - is what makes an
        // unwarmed burst timing-sensitive.
        await GetAsync("/api/markers/source");
        await PostAsync("/api/markers/source");

        var calls = Enumerable.Range(0, 40).Select(i => i % 2 == 0
            ? GetAsync("/api/markers/source")
            : PostAsync("/api/markers/source"));

        var results = await Task.WhenAll(calls);

        for (var i = 0; i < results.Length; i++)
        {
            Assert.Equal(i % 2 == 0 ? "replica" : "master", results[i]);
        }
    }
}
