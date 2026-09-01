using Appouse.EFCore.ReplicaManager;
using Microsoft.EntityFrameworkCore;
using Sample.WebApi;

var builder = WebApplication.CreateBuilder(args);

// 1. Register master/replica splitting. Nothing here touches MVC, so the very same call works
//    unchanged in a Worker Service.
builder.Services.AddEfCoreMasterReplica(options =>
{
    options.MasterConnectionString = builder.Configuration.GetConnectionString("Master")!;
    options.ReplicaConnectionString = builder.Configuration.GetConnectionString("Replica")!;

    // One master, as many replicas as you like. Reads are spread across them round-robin, and a
    // replica that refuses a connection is skipped in favour of the next one.
    foreach (var name in new[] { "Replica2", "Replica3" })
    {
        var replica = builder.Configuration.GetConnectionString(name);
        if (!string.IsNullOrWhiteSpace(replica))
        {
            options.ReplicaConnectionStrings.Add(replica);
        }
    }

    // A replica that just refused a connection is stood down for this long before being tried
    // again, so one dead node does not cost every request a connection timeout.
    options.ReplicaFailureCooldown = TimeSpan.FromSeconds(30);

    // If every replica is unreachable, serve reads from the master rather than failing. Set this to
    // false when the master must not absorb replica traffic.
    options.AllowReplicaFallbackToMaster = true;

    // Anything outside the HTTP pipeline - a health check, a migration - uses the master.
    options.DefaultTarget = DbTarget.Master;
});

// 2. Register the DbContext. The package stays provider-agnostic: you pick the provider, it hands
//    you the master connection string (the one migrations and model building must use).
builder.Services.AddMasterReplicaDbContext<AppDbContext>((options, connectionString) =>
    options.UseSqlServer(connectionString));

// 3. Route controllers and Razor Pages by attribute, then by HTTP verb.
builder.Services.AddControllers().AddDbTargetRouting();

var app = builder.Build();

app.UseRouting();

// 4. Optional, and what makes Minimal API endpoints follow the same rules as controllers.
//    Must come after UseRouting so endpoint metadata - the attributes - is visible.
app.UseDbTargetRouting();

app.MapControllers();

// Minimal APIs: the verb convention applies via the middleware above; these helpers override it.
app.MapGet("/orders/count", async (AppDbContext db) => await db.Orders.CountAsync());

app.MapGet("/orders/{id:int}/fresh", async (int id, AppDbContext db) => await db.Orders.FindAsync(id))
   .UseMasterDb();

app.MapPost("/orders/report", async (AppDbContext db) => await db.Orders.CountAsync())
   .UseReplicaDb();

// A whole group can be pinned at once.
var reports = app.MapGroup("/reports").UseReplicaDb();
reports.MapPost("/daily", async (AppDbContext db) =>
    await db.Orders.GroupBy(o => o.CreatedAt.Date).Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync());

// Migrations must run against the master. DefaultTarget is WriteMaster here so this is already
// correct, but the explicit scope documents the intent and survives a change to that option.
using (var scope = app.Services.CreateScope())
{
    var dbTarget = scope.ServiceProvider.GetRequiredService<IDbTargetContext>();
    using (dbTarget.UseMasterDb())
    {
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }
}

app.Run();
