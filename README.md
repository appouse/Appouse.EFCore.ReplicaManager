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

### The one case failover cannot cover on its own

Opening a connection is not the same as reaching a server. ADO.NET pools connections, so when a
replica dies while the pool still holds warm handles to it, `OpenAsync` hands one back **without a
network round trip and reports success**. The socket is dead, but nothing discovers that until the
first command runs — long after the routing decision, and far too late to pick a different replica
for that attempt.

The package does two things about it, and asks one thing of you:

* A command failing on a connection routed to a replica marks that replica down immediately, so the
  *next* request avoids it instead of drawing another dead handle from the same pool.
* It does not retry the failed command itself. That is EF Core's execution strategy's job.
* **Enable that strategy.** It composes exactly as you would hope: the retry opens a fresh
  connection, which routes to a replica already marked down.

```csharp
builder.Services.AddMasterReplicaDbContext<AppDbContext>((options, cs) =>
    options.UseNpgsql(cs, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 5)));
```

With retry enabled, a replica outage is invisible to application code. Without it, the first request
after the outage surfaces one connection error before traffic settles on the survivor. Both
behaviours are covered by the live tests.

`ReplicaUnavailableException` is deliberately not a transient error: it means every replica was
dialled and refused, so retrying cannot help until one comes back.

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

| Provider | Package | Verified against a live server |
|---|---|---|
| SQL Server | `Microsoft.EntityFrameworkCore.SqlServer` | ✔ |
| PostgreSQL | `Npgsql.EntityFrameworkCore.PostgreSQL` | ✔ |
| MySQL / MariaDB | `Pomelo.EntityFrameworkCore.MySql` | ✔ |
| Oracle | `Oracle.EntityFrameworkCore` | ✔ |
| SQLite | `Microsoft.EntityFrameworkCore.Sqlite` | ✔ |

Every one of these runs against three real database servers — one master and two replicas, each
holding a different marker row so a test can tell which one answered. The integration suite starts
them in containers, proves that reads land on a replica and writes on the master, then **stops a
replica's container mid-test** and proves reads keep being served by the survivor, that a total
outage falls back to the master, and that a recovered node is used again once its cooldown expires.

```bash
dotnet test tests/Appouse.EFCore.ReplicaManager.IntegrationTests   # needs Docker
```

Without Docker those tests skip themselves rather than fail.

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
primary, which the routing rules already guarantee. Note that Oracle's EF Core provider ships **no
`EnableRetryOnFailure`**, unlike the other three, so transparent recovery from a warm-pool outage
needs an execution strategy of your own:

```csharp
options.UseOracle(cs, oracle => oracle.ExecutionStrategy(d => new MyRetryingExecutionStrategy(d)));
```

`tests/Appouse.EFCore.ReplicaManager.IntegrationTests/OracleCluster.cs` has a working one to copy.

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
| `AddDbContext<T>` alone | **Not routed — the host refuses to start** |

`AddEfCoreMasterReplica` registers services but never touches a `DbContext` on its own, so a plain
`AddDbContext` used to produce no error and no warning — just a context that quietly ignored the
ambient target. That now fails at start-up instead. If you register the context yourself, wire the
interceptors in:

```csharp
services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseNpgsql(masterConnectionString);
    options.UseMasterReplicaSplitting(sp);
});
```

---

## Start-up checks

Two ways of wiring this package up wrongly used to produce no error at all, just an application that
looked configured and sent every query to `DefaultTarget`. Both are now caught before the first
request.

**A `DbContext` registered without the interceptors throws.** The message names the context and both
ways to fix it. It is an error rather than a warning because the package is doing literally nothing
for that context.

```
Master/replica splitting is registered, but AppDbContext was registered without it, so every query
from that context ignores the ambient target ... Register the context with
services.AddMasterReplicaDbContext<TContext>(...), or, if you call AddDbContext yourself, add
options.UseMasterReplicaSplitting(serviceProvider) inside it.
```

A second context that deliberately lives elsewhere — an outbox, an audit log, a job store — is
declared rather than silenced:

```csharp
options.UnroutedDbContextTypes.Add(typeof(AuditDbContext));
```

**Controllers registered with no routing mechanism warn.** If the application called
`AddControllers()` but neither `services.AddDbTargetMvcFilter()` nor `app.UseDbTargetRouting()`, a
warning is logged naming both. This one is a warning rather than an error because routing purely
through `UseTarget` scopes is a legitimate design.

Whether the application is a web application is decided by matching service type *names*, never by
touching an MVC type, so the check stays safe on a host with no ASP.NET Core shared framework.

Both checks need an `IHost`; a bare `ServiceProvider` runs no hosted services and so runs no checks.
Turn them off with `options.ValidateStartupWiring = false`.

---

## Dapper and raw ADO.NET

The interceptor fires on the paths **EF Core itself opens**. Reaching past EF Core to the connection
object steps outside that, and the results are worth knowing before you rely on them:

| What you write | Routed? |
|---|---|
| `db.Database.OpenConnectionAsync()`, then Dapper on `GetDbConnection()` | ✔ routed, with failover |
| `db.Database.GetDbConnection()`, then `Open()` yourself | ✘ not routed |
| `db.Database.GetDbConnection()`, then Dapper (which opens it for you) | ✘ not routed |

`GetDbConnection()` hands you the raw `DbConnection`. Opening it yourself — or letting Dapper open it
— is a plain ADO.NET call that EF Core never sees, so nothing rewrites the connection string.

**The sharp edge.** An unrouted connection is not neutral: it carries whatever connection string was
last written to it. After an EF query that went to a replica, EF closes the connection but leaves the
replica's connection string on it, so raw access afterwards inherits that route — even under a
`UseMasterDb()` scope. A Dapper `INSERT` issued that way reaches a read-only replica.

```csharp
using (dbTarget.UseReplicaDb())
{
    await db.Orders.CountAsync();          // routed to a replica
}

// Ambient target is the master again, but the connection still points at the replica:
await db.Database.GetDbConnection().ExecuteAsync("INSERT INTO ...");   // goes to the replica
```

**Do this instead.** Let EF Core open it, then hand the connection to Dapper:

```csharp
await db.Database.OpenConnectionAsync();     // routed, and fails over between replicas
try
{
    var orders = await db.Database.GetDbConnection().QueryAsync<Order>("SELECT ...");
}
finally
{
    await db.Database.CloseConnectionAsync();
}
```

This goes through EF Core's connection pipeline, so it gets the ambient target, replica failover and
health tracking exactly as an EF query would. Use `CloseConnectionAsync()` rather than closing the
connection directly — EF Core reference-counts it.

A connection you construct yourself, rather than taking from a `DbContext`, is outside this package
entirely: resolve `IDbConnectionStringResolver` and choose the string explicitly.

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
| `ValidateStartupWiring` | `true` | Fail fast on a context or a web app that was never wired up. |
| `UnroutedDbContextTypes` | empty | Contexts deliberately left unrouted. |
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
