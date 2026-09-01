using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Appouse.EFCore.ReplicaManager.Tests;

/// <summary>
/// The two ways to wire this package up wrongly without getting an error, and the start-up checks
/// that now turn them into an error and a warning.
/// </summary>
public sealed class StartupWiringTests : IClassFixture<TwoDatabaseFixture>
{
    private readonly TwoDatabaseFixture _fx;

    public StartupWiringTests(TwoDatabaseFixture fx) => _fx = fx;

    private HostApplicationBuilder Builder(Action<MasterReplicaOptions>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddEfCoreMasterReplica(options =>
        {
            options.MasterConnectionString = _fx.MasterConnectionString;
            options.ReplicaConnectionString = _fx.ReplicaConnectionString;
            configure?.Invoke(options);
        });
        return builder;
    }

    [Fact]
    public void A_DbContext_registered_without_the_interceptors_stops_the_host()
    {
        var builder = Builder();
        builder.Services.AddDbContext<MarkerContext>(o => o.UseSqlite(_fx.MasterConnectionString));

        using var host = builder.Build();

        var error = Assert.Throws<InvalidOperationException>(() => host.Start());

        Assert.Contains(nameof(MarkerContext), error.Message, StringComparison.Ordinal);
        Assert.Contains("AddMasterReplicaDbContext", error.Message, StringComparison.Ordinal);
        Assert.Contains("UseMasterReplicaSplitting", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_properly_registered_DbContext_starts_cleanly()
    {
        var builder = Builder();
        builder.Services.AddMasterReplicaDbContext<MarkerContext>((o, cs) => o.UseSqlite(cs));

        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task A_context_you_registered_yourself_passes_once_the_interceptors_are_added()
    {
        var builder = Builder();
        builder.Services.AddDbContext<MarkerContext>((sp, o) =>
        {
            o.UseSqlite(_fx.MasterConnectionString);
            o.UseMasterReplicaSplitting(sp);
        });

        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task A_context_listed_as_unrouted_is_left_alone()
    {
        var builder = Builder(options => options.UnroutedDbContextTypes.Add(typeof(MarkerContext)));
        builder.Services.AddDbContext<MarkerContext>(o => o.UseSqlite(_fx.MasterConnectionString));

        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task The_check_can_be_turned_off_entirely()
    {
        var builder = Builder(options => options.ValidateStartupWiring = false);
        builder.Services.AddDbContext<MarkerContext>(o => o.UseSqlite(_fx.MasterConnectionString));

        using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task Controllers_without_any_routing_mechanism_produce_a_warning()
    {
        var recorder = new WarningRecorder();

        var builder = Builder();
        builder.Logging.AddProvider(recorder);
        builder.Services.AddControllers();
        builder.Services.AddMasterReplicaDbContext<MarkerContext>((o, cs) => o.UseSqlite(cs));

        using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        Assert.Contains(recorder.Warnings, w => w.Contains("AddDbTargetMvcFilter", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Registering_the_mvc_filter_silences_the_warning()
    {
        var recorder = new WarningRecorder();

        var builder = Builder();
        builder.Logging.AddProvider(recorder);
        builder.Services.AddControllers();
        builder.Services.AddDbTargetMvcFilter();
        builder.Services.AddMasterReplicaDbContext<MarkerContext>((o, cs) => o.UseSqlite(cs));

        using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        Assert.DoesNotContain(recorder.Warnings, w => w.Contains("AddDbTargetMvcFilter", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_non_web_application_is_never_warned_about_mvc()
    {
        var recorder = new WarningRecorder();

        var builder = Builder();
        builder.Logging.AddProvider(recorder);
        builder.Services.AddMasterReplicaDbContext<MarkerContext>((o, cs) => o.UseSqlite(cs));

        using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        Assert.DoesNotContain(recorder.Warnings, w => w.Contains("AddDbTargetMvcFilter", StringComparison.Ordinal));
    }

    private sealed class WarningRecorder : ILoggerProvider
    {
        public List<string> Warnings { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Recorder(this);

        public void Dispose()
        {
        }

        private sealed class Recorder(WarningRecorder owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Warning)
                {
                    lock (owner.Warnings)
                    {
                        owner.Warnings.Add(formatter(state, exception));
                    }
                }
            }
        }
    }
}
