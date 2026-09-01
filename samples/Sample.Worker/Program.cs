using Appouse.EFCore.ReplicaManager;
using Microsoft.EntityFrameworkCore;
using Sample.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Identical to the web application's registration - no HTTP concepts involved, and deliberately no
// MVC types, so this app needs no ASP.NET Core runtime.
builder.Services.AddEfCoreMasterReplica(options =>
{
    options.MasterConnectionString = builder.Configuration.GetConnectionString("Master")!;
    options.ReplicaConnectionString = builder.Configuration.GetConnectionString("Replica")!;

    var secondReplica = builder.Configuration.GetConnectionString("Replica2");
    if (!string.IsNullOrWhiteSpace(secondReplica))
    {
        options.ReplicaConnectionStrings.Add(secondReplica);
    }

    // Outside HTTP there is no verb to infer from, so unattributed work uses the master unless a
    // UseTarget scope says otherwise. That is the safe default for a worker.
    options.DefaultTarget = DbTarget.Master;
});

builder.Services.AddMasterReplicaDbContext<AppDbContext>((options, connectionString) =>
    options.UseSqlServer(connectionString));

builder.Services.AddHostedService<SettlementWorker>();

var host = builder.Build();
await host.RunAsync();
