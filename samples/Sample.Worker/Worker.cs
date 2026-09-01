using Appouse.EFCore.ReplicaManager;
using Microsoft.EntityFrameworkCore;

namespace Sample.Worker;

/// <summary>
/// A background job with no <c>HttpContext</c> anywhere in sight. Routing is entirely explicit:
/// scan the replica, settle on the master.
/// </summary>
/// <remarks>
/// The same pattern applies unchanged to a Hangfire job, a Quartz job or any other activator: the
/// target lives in an <c>AsyncLocal</c> owned by a singleton, so it needs nothing from the web
/// stack. Note that this project is a <c>Microsoft.NET.Sdk.Worker</c> app with no ASP.NET Core
/// dependency at all, and it still builds and runs - the package's MVC types are never touched by
/// any method this app calls.
/// </remarks>
public sealed class SettlementWorker(
    IServiceScopeFactory scopeFactory,
    IDbTargetContext dbTarget,
    ILogger<SettlementWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Settlement pass failed; retrying at the next interval.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        // One DI scope per pass, exactly as a request would have.
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1. The heavy scan is lag-tolerant, so keep it off the master.
        List<int> candidates;
        using (dbTarget.UseReadDb())
        {
            candidates = await db.Orders
                .Where(o => !o.Settled && o.CreatedAt < DateTimeOffset.UtcNow.AddHours(-1))
                .Select(o => o.Id)
                .Take(500)
                .ToListAsync(cancellationToken);
        }

        if (candidates.Count == 0)
        {
            logger.LogDebug("Nothing to settle.");
            return;
        }

        // 2. Re-read and update on the master, where the rows are authoritative.
        using (dbTarget.UseWriteDb())
        {
            var orders = await db.Orders
                .Where(o => candidates.Contains(o.Id) && !o.Settled)
                .ToListAsync(cancellationToken);

            foreach (var order in orders)
            {
                order.Settled = true;
            }

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Settled {Count} orders.", orders.Count);
        }
    }
}
