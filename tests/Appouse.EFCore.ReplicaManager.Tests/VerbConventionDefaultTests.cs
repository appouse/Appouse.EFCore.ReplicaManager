using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Appouse.EFCore.ReplicaManager.Tests;

/// <summary>
/// Adding this package to an existing application must not move any traffic on its own. Routing is
/// explicit: without an attribute or a scope, a request goes to the configured default and nowhere
/// else, whatever its HTTP verb.
/// </summary>
public sealed class VerbConventionDefaultTests : IClassFixture<TwoDatabaseFixture>, IAsyncLifetime
{
    private readonly TwoDatabaseFixture _fx;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public VerbConventionDefaultTests(TwoDatabaseFixture fx) => _fx = fx;

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

            // Nothing else: RouteByHttpMethod stays off, DefaultTarget stays Master.
        });
        builder.Services.AddMasterReplicaDbContext<MarkerContext>((options, cs) => options.UseSqlite(cs));

        _app = builder.Build();
        _app.MapControllers();

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task<string> PostAsync(string url)
    {
        var response = await _client.PostAsync(url, content: null);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task An_unannotated_GET_stays_on_the_default_target()
        => Assert.Equal("master", await _client.GetStringAsync("/api/markers/source"));

    [Fact]
    public async Task An_unannotated_POST_stays_on_the_default_target()
        => Assert.Equal("master", await PostAsync("/api/markers/source"));

    [Fact]
    public async Task Attributes_still_work_without_the_verb_convention()
    {
        Assert.Equal("master", await _client.GetStringAsync("/api/markers/forced-write"));
        Assert.Equal("replica", await PostAsync("/api/markers/forced-read"));
        Assert.Equal("replica", await PostAsync("/api/reporting/inherited"));
        Assert.Equal("master", await PostAsync("/api/reporting/overridden"));
    }
}
