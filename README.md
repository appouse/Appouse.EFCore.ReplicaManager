# Appouse.EFCore.ReplicaManager

Transparent **master/replica** database splitting for **EF Core 8+**.

One master, as many replicas as you like. `GET` requests read from a replica, writes go to the
master, and a replica that stops answering is skipped in favour of the next one. A `using` block
gives you explicit control anywhere else — including background workers, where there is no
`HttpContext` to hang routing off. Provider agnostic: SQL Server, PostgreSQL, MySQL, SQLite and
anything else with an EF Core provider.

```bash
dotnet add package Appouse.EFCore.ReplicaManager
```

---

## 30 seconds

```csharp
using Appouse.EFCore.ReplicaManager;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEfCoreMasterReplica(options =>
{
    options.MasterConnectionString  = builder.Configuration.GetConnectionString("Master")!;
    options.ReplicaConnectionString = builder.Configuration.GetConnectionString("Replica1")!;
    options.ReplicaConnectionStrings.Add(builder.Configuration.GetConnectionString("Replica2")!);
    options.DefaultTarget = DbTarget.Master;
});

// You choose the provider; the package hands you the connection string.
builder.Services.AddMasterReplicaDbContext<AppDbContext>((options, connectionString) =>
    options.UseSqlServer(connectionString));

builder.Services.AddControllers().AddDbTargetRouting();

var app = builder.Build();

app.UseRouting();
app.UseDbTargetRouting();   // optional: extends the same rules to Minimal APIs
app.MapControllers();
app.Run();
```

Your `DbContext` needs no changes at all. No base class, no second context, no repository layer.

---

## Routing rules

Resolved in this order — the first match wins:

| # | Rule | Result |
|---|------|--------|
| 1 | `[UseMasterDb]` / `[UseReplicaDb]` on the **action** | that target |
| 2 | `[UseMasterDb]` / `[UseReplicaDb]` on the **controller** | that target |
| 3 | `SaveChanges` is running | **always** `Master` |
| 4 | A transaction is active | **always** `Master` |
| 5 | An enclosing `UseTarget(...)` scope | that target |
| 6 | HTTP verb — `GET`, `HEAD`, `OPTIONS`, `TRACE` | `Replica` |
| 7 | HTTP verb — everything else | `Master` |
| 8 | Nothing above applies | `options.DefaultTarget` |

Unknown verbs fall back to the master: guessing wrong towards a replica breaks writes, guessing
wrong towards the master only costs a little capacity.

---

## Many replicas, and what happens when one dies

The topology is **one master and any number of replicas**. Reads are spread across the replicas
round-robin. When a replica refuses a connection, the package does not surface the error: it opens
the connection against the next replica instead, and only gives up once every replica has been
tried.

```csharp
options.MasterConnectionString  = "...";              // exactly one
options.ReplicaConnectionString = "...replica-1...";  // first replica
options.ReplicaConnectionStrings.Add("...replica-2...");
options.ReplicaConnectionStrings.Add("...replica-3...");
```

What happens, in order:

1. `IReplicaSelector` picks a starting replica — round-robin by default.
2. Each replica is dialled in turn until one accepts the connection.
3. A replica that refused is stood down for `ReplicaFailureCooldown` (30 seconds by default), so one
   dead node does not cost *every* request a connection timeout. It is moved to the back of the
   queue, never banned — if all the others fail too, it is still tried.
4. If no replica answers, the read is served by the master, unless
   `AllowReplicaFallbackToMaster` is `false` — then a `ReplicaUnavailableException` is thrown,
   carrying every provider failure in an `AggregateException`.

Writes are never failed over, because there is only one master to write to. Retries there are left
to EF Core's own execution strategy.

Swap the strategy for weighted, latency-aware or locality-aware selection:

```csharp
services.Replace(ServiceDescriptor.Singleton<IReplicaSelector, MyReplicaSelector>());
services.Replace(ServiceDescriptor.Singleton<IReplicaHealthMonitor, MyHealthMonitor>());
```

---

## Attributes

```csharp
[ApiController]
[Route("api/orders")]
public sealed class OrdersController(AppDbContext db) : ControllerBase
{
    [HttpGet]                       // -> replica, by convention
    public Task<List<Order>> List() => db.Orders.ToListAsync();

    [HttpPost]                      // -> master, by convention
    public async Task<Order> Create(Order order) { /* ... */ }

    [HttpGet("{id:int}")]
    [UseMasterDb]                   // a GET that must not read stale data
    public Task<Order?> Get(int id) => db.Orders.FindAsync(id).AsTask();

    [HttpPost("search")]
    [UseReplicaDb]                  // a POST that only reads
    public Task<List<Order>> Search(SearchRequest r) { /* ... */ }
}
```

An attribute on the action always beats one on the controller.

### Minimal APIs

```csharp
app.MapGet("/orders/{id:int}", handler).UseMasterDb();
app.MapPost("/orders/report", handler).UseReplicaDb();

var reports = app.MapGroup("/reports").UseReplicaDb();   // applies to the whole group
```

> A Minimal API endpoint is only routed if you either add `app.UseDbTargetRouting()` to the pipeline
> or use one of the helpers above. Without either, it falls back to `DefaultTarget`.

---

## Background workers, Hangfire, Quartz

This is the part that `HttpContext`-based approaches cannot do. `IDbTargetContext` is a singleton
whose state lives in an `AsyncLocal`, so it works identically outside the web stack:

```csharp
public sealed class SettlementWorker(
    IServiceScopeFactory scopeFactory,
    IDbTargetContext dbTarget) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The heavy scan is lag-tolerant: keep it off the master.
        List<int> candidates;
        using (dbTarget.UseReplicaDb())
        {
            candidates = await db.Orders.Where(o => !o.Settled).Select(o => o.Id).ToListAsync(stoppingToken);
        }

        // Update where the rows are authoritative.
        using (dbTarget.UseMasterDb())
        {
            /* ... */
            await db.SaveChangesAsync(stoppingToken);
        }
    }
}
```

Registration is the same call as in the web app — `AddEfCoreMasterReplica` deliberately touches no
ASP.NET Core type, so a `Microsoft.NET.Sdk.Worker` project on a runtime-only container image builds
and runs with no ASP.NET Core shared framework installed.

---

## Replication lag: read your own writes

The single most common way master/replica splitting breaks an application is a write followed
immediately by a read that lands on a replica which has not caught up.

Two mechanisms handle this, both on by default:

* **`ForceMasterOnSaveChanges`** — `SaveChanges` switches to the master *before* touching the
  database. A `GET` action that stamps `LastSeenAt` or writes an audit row therefore works, instead
  of failing against a read-only replica.
* **`StickToMasterAfterSaveChanges`** — after a successful save, the rest of the scope stays on the
  master. Within a request, everything read after a write is read back from the master.

Beyond the current scope the package **cannot** detect lag; nothing can, from inside the
application. If a later request must observe an earlier write, pin it explicitly:

```csharp
using (dbTarget.UseMasterDb())
{
    var order = await db.Orders.FindAsync(id);
}
```

---

## Migrations

`MasterConnectionString` is the canonical connection string: it is what the provider gets for model
building, what `dotnet ef` uses, and what `Database.Migrate()` connects to. Keep `DefaultTarget` at
`Master`, or wrap migration explicitly:

```csharp
using (dbTarget.UseMasterDb())
{
    await db.Database.MigrateAsync();
}
```

---

## Database providers

The package never names a provider type. Its entire provider-facing surface is four members declared
on the ADO.NET base class — `DbConnection.State`, `DbConnection.ConnectionString`, `Open`/`OpenAsync`
and `Close` — so any EF Core relational provider works.

| Provider | Package | Verified |
|---|---|---|
| SQL Server | `Microsoft.EntityFrameworkCore.SqlServer` | ✔ |
| PostgreSQL | `Npgsql.EntityFrameworkCore.PostgreSQL` | ✔ |
| MySQL / MariaDB | `Pomelo.EntityFrameworkCore.MySql` | ✔ |
| Oracle | `Oracle.EntityFrameworkCore` | ✔ |
| SQLite | `Microsoft.EntityFrameworkCore.Sqlite` | ✔ |

*Verified* means the test suite runs the routing and failover paths against each provider's real
connection and exception types: connection-string reassignment on a closed connection, the failover
loop visiting every replica, and the fallback handing the operation back to EF Core. A live server is
only needed to prove that a successful open succeeds, which is ordinary provider behaviour.

```csharp
builder.Services.AddMasterReplicaDbContext<AppDbContext>((options, cs) => options.UseSqlServer(cs));
builder.Services.AddMasterReplicaDbContext<AppDbContext>((options, cs) => options.UseNpgsql(cs));
builder.Services.AddMasterReplicaDbContext<AppDbContext>((options, cs) => options.UseOracle(cs));
builder.Services.AddMasterReplicaDbContext<AppDbContext>((options, cs) => options.UseSqlite(cs));
builder.Services.AddMasterReplicaDbContext<AppDbContext>((options, cs) =>
    options.UseMySql(cs, new MySqlServerVersion(new Version(8, 0, 34))));
```

### Provider notes

**MySQL — do not use `ServerVersion.AutoDetect`.** It opens a connection while the service provider
is being built, so application start-up would depend on the master being reachable, before any
routing exists. Pass an explicit `MySqlServerVersion` as above.

**SQL Server — mark the replica connection strings read-only.** When the replicas are Always On
secondaries, add `ApplicationIntent=ReadOnly` to each replica connection string so the listener
routes to a readable secondary. The master connection string must not carry it.

**PostgreSQL — Npgsql's multi-host support is complementary, not redundant.** Npgsql can take
`Host=h1,h2,h3` with `Target Session Attributes=prefer-standby` and `Load Balance Hosts=true` and do
its own connection-level balancing. That still cannot know a `GET` from a `POST`, which is what this
package decides. Use either style: several `ReplicaConnectionStrings` entries, or one multi-host
replica string — or both.

**Oracle — an Active Data Guard standby is read-only.** It works as a replica; writes must reach the
primary, which the routing rules already guarantee.

**SQLite has no replication.** It is genuinely useful for tests — the suite in this repository routes
between two real SQLite files to prove which database served a query — but it is not a production
topology.

**All providers: each connection string gets its own ADO.NET connection pool.** A master plus three
replicas is four pools, each with its own `Max Pool Size`. Size them for the traffic each node
actually takes rather than copying one number across all four.

**All providers: the model is built from the master connection string.** Replicas must expose the
same schema. EF Core builds one model, and migrations run against the master.

---

## Registering the DbContext

| Registration | Result |
|---|---|
| `AddMasterReplicaDbContext<T>` | Routed |
| `AddMasterReplicaDbContextPool<T>` | Routed; no routing state sticks to a pooled instance |
| `AddMasterReplicaDbContextFactory<T>` | Routed; a produced context follows the flow that *uses* it |
| `AddDbContext<T>` + `UseMasterReplicaSplitting(sp)` | Routed, identically |
| `AddDbContext<T>` alone | **Silently not routed** |

The last row is the one trap: `AddEfCoreMasterReplica` registers services but never touches a
`DbContext` on its own, so a plain `AddDbContext` produces no error and no warning — just a context
that ignores the ambient target. If you register it yourself, wire the interceptors in:

```csharp
services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseNpgsql(masterConnectionString);
    options.UseMasterReplicaSplitting(sp);
});
```

---

## Options

| Option | Default | Meaning |
|---|---|---|
| `MasterConnectionString` | *(required)* | The single master. |
| `ReplicaConnectionString` | *(required)* | First replica. Set it equal to the master to disable splitting. |
| `ReplicaConnectionStrings` | empty | Additional replicas, load-balanced and failed over. |
| `DefaultTarget` | `Master` | Used when no rule applies. |
| `RouteByHttpMethod` | `true` | Apply the verb convention. |
| `ForceMasterInsideTransaction` | `true` | Transactions always use the master. |
| `ForceMasterOnSaveChanges` | `true` | `SaveChanges` always uses the master. |
| `StickToMasterAfterSaveChanges` | `true` | Read-after-write consistency within the scope. |
| `AllowReplicaFallbackToMaster` | `true` | Use the master when no replica answers. |
| `ReplicaFailureCooldown` | `30s` | How long a failed replica is stood down. |
| `MvcActionFilterOrder` | `int.MinValue` | Filter position; lowest runs first. |

Bind from configuration instead, if you prefer:

```csharp
builder.Services.AddEfCoreMasterReplica(
    builder.Configuration.GetSection(MasterReplicaOptions.SectionName));   // "EfCoreMasterReplica"
```

Configuration is validated at host start-up, so a missing connection string stops the application
instead of surfacing as a failure on the first query.

---

## How it works

A singleton `DbConnectionInterceptor` rewrites `DbConnection.ConnectionString` in
`ConnectionOpening` / `ConnectionOpeningAsync`, immediately before EF Core opens the connection. For
a replica read it suppresses EF Core's own open and performs it, which is the only way to retry a
different connection string within one operation — that is what makes failover possible.

Three design decisions are load-bearing:

* **Everything is a singleton.** Interceptors are captured inside `DbContextOptions`, and EF Core
  keys its internal service-provider cache on those options. A per-scope interceptor would make EF
  build a fresh internal provider per request — unbounded memory growth and EF's *"More than twenty
  IServiceProvider instances have been created"* warning. All per-request state lives in an
  `AsyncLocal` instead, which is also why no `HttpContext` is needed.
* **The ambient value is a mutable holder, not a bare value.** Assigning `AsyncLocal<T>.Value` is
  invisible to callers further up the stack, so a write detected deep inside `SaveChangesAsync`
  could never pin the master for the rest of the request. Storing a small mutable object and
  mutating a field on it makes that work, while `UseTarget` installs a *new* holder so its effect
  stays flow-local and is undone on `Dispose`.
* **No MVC type is referenced from any method a non-web host calls.** The CLR resolves type
  references when it JITs a method, before executing it, so a single mention of `MvcOptions` in
  `AddEfCoreMasterReplica` would crash a Worker Service on a runtime-only image — a `try`/`catch`
  around it would not help. MVC wiring lives in `AddDbTargetMvcFilter`, and the package's
  `FrameworkReference` carries `PrivateAssets="all"` so the ASP.NET Core shared framework never
  becomes a consumer's runtime requirement.

### Limitations

* A connection string can only be assigned while the connection is **closed**. EF Core opens late
  and closes early, so each operation is routed independently — but not while an explicit
  transaction is open, or after `Database.OpenConnection()`. Start that work inside a
  `UseTarget(...)` scope; the route is fixed when the connection opens.
* Deferred execution follows the target in effect when the query *runs*, not when it is composed.
  An `IQueryable` built inside a scope and enumerated outside it uses the outer target.
* Each connection string gets its own ADO.NET connection pool. Size `Max Pool Size` accordingly.
* Connection strings are never written to the log, at any level.

---

## License

MIT. See [LICENSE](https://github.com/appouse/Appouse.EFCore.ReplicaManager/blob/main/LICENSE).
