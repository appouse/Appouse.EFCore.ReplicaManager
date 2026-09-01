using Appouse.EFCore.ReplicaManager;
using Microsoft.EntityFrameworkCore;
using Sample.WebApi;

var builder = WebApplication.CreateBuilder(args);

// 1. Register read/write splitting. Nothing here touches MVC, so the very same call works
//    unchanged in a Worker Service.
builder.Services.AddEfCoreReadWriteSplit(options =>
{
    options.WriteConnectionString = builder.Configuration.GetConnectionString("Master")!;
    options.ReadConnectionString = builder.Configuration.GetConnectionString("Replica")!;

    // Optional: spread reads over more than one replica.
    var secondReplica = builder.Configuration.GetConnectionString("Replica2");
    if (!string.IsNullOrWhiteSpace(secondReplica))
    {
        options.ReadConnectionStrings.Add(secondReplica);
    }

    // Anything outside the HTTP pipeline - a health check, a migration - uses the master.
    options.DefaultTarget = DbTarget.WriteMaster;
});

// 2. Register the DbContext. The package stays provider-agnostic: you pick the provider, it hands
//    you the master connection string (the one migrations and model building must use).
builder.Services.AddReadWriteDbContext<AppDbContext>((options, connectionString) =>
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
   .UseWriteDb();

app.MapPost("/orders/report", async (AppDbContext db) => await db.Orders.CountAsync())
   .UseReadDb();

// A whole group can be pinned at once.
var reports = app.MapGroup("/reports").UseReadDb();
reports.MapPost("/daily", async (AppDbContext db) =>
    await db.Orders.GroupBy(o => o.CreatedAt.Date).Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync());

// Migrations must run against the master. DefaultTarget is WriteMaster here so this is already
// correct, but the explicit scope documents the intent and survives a change to that option.
using (var scope = app.Services.CreateScope())
{
    var dbTarget = scope.ServiceProvider.GetRequiredService<IDbTargetContext>();
    using (dbTarget.UseWriteDb())
    {
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }
}

app.Run();
