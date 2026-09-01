using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Appouse.EFCore.ReplicaManager.Tests;

/// <summary>
/// The other supported wiring: one middleware routes everything - controllers and Minimal APIs
/// alike - with no MVC filter registered at all.
/// </summary>
public sealed class MiddlewareRoutingTests : IClassFixture<TwoDatabaseFixture>, IAsyncLifetime
{
    private readonly TwoDatabaseFixture _fx;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public MiddlewareRoutingTests(TwoDatabaseFixture fx) => _fx = fx;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddControllers().AddApplicationPart(typeof(MarkersController).Assembly);

        builder.Services.AddEfCoreMasterReplica(options =>
        {
            options.MasterConnectionString = _fx.MasterConnectionString;
            options.ReplicaConnectionString = _fx.ReplicaConnectionString;
        });
        builder.Services.AddMasterReplicaDbContext<MarkerContext>((options, cs) => options.UseSqlite(cs));

        _app = builder.Build();

        _app.UseRouting();
        _app.UseDbTargetRouting();

        _app.MapControllers();
        _app.MapGet("/minimal/get", (MarkerContext db) => MarkersController.Source(db));
        _app.MapPost("/minimal/post", (MarkerContext db) => MarkersController.Source(db));
        _app.MapGet("/minimal/forced-write", (MarkerContext db) => MarkersController.Source(db)).UseMasterDb();

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
    public async Task The_middleware_applies_the_verb_convention_to_minimal_apis()
    {
        Assert.Equal("replica", await GetAsync("/minimal/get"));
        Assert.Equal("master", await PostAsync("/minimal/post"));
    }

    [Fact]
    public async Task The_middleware_applies_the_verb_convention_to_controllers()
    {
        Assert.Equal("replica", await GetAsync("/api/markers/source"));
        Assert.Equal("master", await PostAsync("/api/markers/source"));
    }

    [Fact]
    public async Task The_middleware_honours_attributes_on_controllers()
    {
        Assert.Equal("master", await GetAsync("/api/markers/forced-write"));
        Assert.Equal("replica", await PostAsync("/api/markers/forced-read"));
        Assert.Equal("master", await PostAsync("/api/reporting/overridden"));
        Assert.Equal("replica", await PostAsync("/api/reporting/inherited"));
    }

    [Fact]
    public async Task The_middleware_honours_minimal_api_helpers()
        => Assert.Equal("master", await GetAsync("/minimal/forced-write"));
}
