# Appouse.EFCore.ReplicaManager

**English** · [Türkçe](https://github.com/appouse/Appouse.EFCore.ReplicaManager/blob/main/README.tr.md)

Transparent **master/replica** database splitting for **EF Core 8+**.

One master, as many replicas as you like. You say which queries go where — with an attribute, a
`using` block, or a default — and a replica that stops answering is skipped in favour of the next
one. Provider agnostic: SQL Server, PostgreSQL, MySQL, Oracle, SQLite and anything else with an EF
Core provider.

```bash
dotnet add package Appouse.EFCore.ReplicaManager
```

**Adding this package moves no traffic on its own.** Routing is explicit. A query with no attribute
and no scope goes to `DefaultTarget` and nowhere else, so you can install it into a running
application and turn routing on one endpoint at a time.

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
| 6 | HTTP verb — *only if you enable `RouteByHttpMethod`* | `GET`/`HEAD`/`OPTIONS`/`TRACE` → `Replica`, else `Master` |
| 7 | Nothing above applies | `options.DefaultTarget` |

Rule 6 is **off by default**. A `GET` is not reliably a read — plenty of them stamp a
`LastSeenAt`, fill a cache or write an audit row — so moving every one of them onto a replica is a
behaviour change you should opt into deliberately:

```csharp
options.RouteByHttpMethod = true;
```

Once enabled, unknown verbs fall back to the master: guessing wrong towards a replica breaks writes,
guessing wrong towards the master only costs a little capacity.

---

## Saying where a query goes

**By attribute**, per action or per controller — an attribute on the action always wins:

```csharp
[HttpGet("{id:int}")]
[UseMasterDb]                   // a GET that must not read stale data
public Task<Order?> Get(int id) => db.Orders.FindAsync(id).AsTask();

[HttpPost("search")]
[UseReplicaDb]                  // a POST that only reads
public Task<List<Order>> Search(SearchRequest r) { /* ... */ }
```

**By scope**, anywhere — including where there is no `HttpContext` at all:

```csharp
using (dbTarget.UseReplicaDb())
{
    var ids = await db.Orders.Where(o => !o.Settled).Select(o => o.Id).ToListAsync();
}
```

**By Minimal API helper**, per endpoint or per group:

```csharp
app.MapGet("/orders/{id:int}", handler).UseMasterDb();
var reports = app.MapGroup("/reports").UseReplicaDb();
```

> A Minimal API endpoint is routed only if you use one of those helpers or add
> `app.UseDbTargetRouting()` to the pipeline. Without either it falls back to `DefaultTarget`.

---

## Many replicas, and what happens when one dies

The topology is **one master and any number of replicas**. Replica reads are spread round-robin.
When a replica refuses a connection, the package does not surface the error: it opens the connection
against the next replica instead, and only gives up once every replica has been tried.

```csharp
options.MasterConnectionString  = "...";              // exactly one
options.ReplicaConnectionString = "...replica-1...";
options.ReplicaConnectionStrings.Add("...replica-2...");
options.ReplicaConnectionStrings.Add("...replica-3...");
```

1. `IReplicaSelector` picks a starting replica — round-robin by default.
2. Each replica is dialled in turn until one accepts the connection.
3. A replica that refused is stood down for `ReplicaFailureCooldown` (30 seconds by default), so one
   dead node does not cost *every* request a connection timeout. It is moved to the back of the
   queue, never banned — if all the others fail too, it is still tried.
4. If no replica answers, the read is served by the master, unless `AllowReplicaFallbackToMaster` is
   `false` — then a `ReplicaUnavailableException` is thrown, carrying every provider failure in an
   `AggregateException`.

Writes are never failed over, because there is only one master to write to.

### The one case failover cannot cover on its own

Opening a connection is not the same as reaching a server. ADO.NET pools connections, so when a
replica dies while the pool still holds warm handles to it, `OpenAsync` hands one back **without a
network round trip and reports success**. The socket is dead, but nothing discovers that until the
first command runs — far too late to pick a different replica for that attempt.

* A command failing on a connection routed to a replica marks that replica down immediately, so the
  *next* request avoids it instead of drawing another dead handle from the same pool.
* The failed command is not retried here — that is EF Core's execution strategy's job.
* **Enable that strategy.** The retry opens a fresh connection, which routes away from the node
  already marked down.

```csharp
builder.Services.AddMasterReplicaDbContext<AppDbContext>((options, cs) =>
    options.UseNpgsql(cs, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 5)));
```

With retry enabled a replica outage is invisible to application code. Without it, the first request
after the outage surfaces one connection error before traffic settles on the survivor. Both are
covered by the live tests.

`ReplicaUnavailableException` is deliberately not transient: every replica was dialled and refused,
so retrying cannot help until one comes back.

---

## Background workers, Hangfire, Quartz

`IDbTargetContext` is a singleton whose state lives in an `AsyncLocal`, so it works identically
outside the web stack:

```csharp
public sealed class SettlementWorker(
    IServiceScopeFactory scopeFactory,
    IDbTargetContext dbTarget) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        List<int> candidates;
        using (dbTarget.UseReplicaDb())          // heavy, lag-tolerant scan
        {
            candidates = await db.Orders.Where(o => !o.Settled).Select(o => o.Id).ToListAsync(stoppingToken);
        }

        using (dbTarget.UseMasterDb())           // authoritative update
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

## Dapper and raw ADO.NET

The interceptor fires on the paths **EF Core itself opens**. Reaching past it to the connection
object steps outside that:

| What you write | Routed? |
|---|---|
| `db.Database.OpenRoutedConnectionAsync(...)` | ✔ routed, with failover |
| `db.Database.OpenConnectionAsync()`, then Dapper on `GetDbConnection()` | ✔ routed, with failover |
| `db.Database.GetDbConnection()`, then `Open()` yourself | ✘ not routed |
| `db.Database.GetDbConnection()`, then Dapper (which opens it for you) | ✘ not routed |

**The sharp edge.** An unrouted connection is not neutral: it carries whatever connection string was
last written to it. After an EF query that went to a replica, EF closes the connection but leaves the
replica's connection string on it, so raw access afterwards inherits that route — even under a
`UseMasterDb()` scope. A Dapper `INSERT` issued that way reaches a read-only replica.

**Do this instead.** Say which database you want, and let EF Core open it:

```csharp
// The master.
await using var routed = await db.Database.OpenRoutedConnectionAsync(DbTarget.Master, cancellationToken);
var order = await routed.Connection.QuerySingleAsync<Order>("SELECT * FROM Orders WHERE Id = @id", new { id });

// A healthy replica, with failover between them.
await using var routed = await db.Database.OpenRoutedConnectionAsync(DbTarget.Replica, cancellationToken);
var rows = await routed.Connection.QueryAsync<Row>("SELECT ... /* heavy, lag-tolerant */");

// Say nothing and you get the target already in effect - an enclosing UseTarget scope if there is
// one, and your configured DefaultTarget otherwise.
await using var routed = await db.Database.OpenRoutedConnectionAsync(cancellationToken);
```

There is a synchronous `OpenRoutedConnection()` with the same two shapes. Disposing the handle
returns the connection to the context rather than closing it directly, because EF Core
reference-counts it; disposing twice is a no-op.

The target binds while the connection is being opened. If the context is already holding an open
connection, EF Core hands back the existing one and its route stands — a connection string cannot be
changed while the connection is open.

A connection you construct yourself, rather than taking from a `DbContext`, is outside this package
entirely: resolve `IDbConnectionStringResolver` and choose the string explicitly.

---

## Read your own writes

Two mechanisms, both on by default:

* **`ForceMasterOnSaveChanges`** — `SaveChanges` switches to the master *before* touching the
  database, so a `GET` action that stamps `LastSeenAt` or writes an audit row works instead of
  failing against a read-only replica.
* **`StickToMasterAfterSaveChanges`** — after a successful save, the rest of the scope stays on the
  master, so everything read after a write is read back from the master.

Beyond the current scope the package cannot detect replication lag; nothing can, from inside the
application. If a later request must observe an earlier write, pin it explicitly with
`UseMasterDb()`.

---

## Start-up checks

Two ways of wiring this package up wrongly used to produce no error at all. Both are now caught
before the first request.

**A `DbContext` registered without the interceptors throws.** The message names the context and both
ways to fix it. It is an error rather than a warning because the package is doing literally nothing
for that context. A second context that deliberately lives elsewhere is declared rather than
silenced:

```csharp
options.UnroutedDbContextTypes.Add(typeof(AuditDbContext));
```

**Controllers registered with no routing mechanism warn.** If the application called
`AddControllers()` but neither `services.AddDbTargetMvcFilter()` nor `app.UseDbTargetRouting()`, a
warning is logged naming both. A warning rather than an error, because routing purely through
`UseTarget` scopes is a legitimate design.

Whether the application is a web application is decided by matching service type *names*, never by
touching an MVC type, so the check stays safe on a host with no ASP.NET Core shared framework. Both
checks need an `IHost`. Turn them off with `options.ValidateStartupWiring = false`.

---

## Registering the DbContext

| Registration | Result |
|---|---|
| `AddMasterReplicaDbContext<T>` | Routed |
| `AddMasterReplicaDbContextPool<T>` | Routed; no routing state sticks to a pooled instance |
| `AddMasterReplicaDbContextFactory<T>` | Routed; a produced context follows the flow that *uses* it |
| `AddDbContext<T>` + `UseMasterReplicaSplitting(sp)` | Routed, identically |
| `AddDbContext<T>` alone | Not routed — the host refuses to start |

```csharp
services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseNpgsql(masterConnectionString);
    options.UseMasterReplicaSplitting(sp);
});
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
them in containers, then **stops a replica's container mid-test** and proves reads keep being served
by the survivor, that a total outage falls back to the master, and that a recovered node is used
again once its cooldown expires.

```bash
dotnet test tests/Appouse.EFCore.ReplicaManager.IntegrationTests   # needs Docker
```

Without Docker those tests skip themselves rather than fail.

### Provider notes

**MySQL — do not use `ServerVersion.AutoDetect`.** It opens a connection while the service provider
is being built, so start-up would depend on the master being reachable before any routing exists.
Pass an explicit `MySqlServerVersion`.

**SQL Server — mark the replica connection strings read-only.** When the replicas are Always On
secondaries, add `ApplicationIntent=ReadOnly` to each replica connection string. The master
connection string must not carry it.

**PostgreSQL — Npgsql's multi-host support is complementary.** It can balance connections itself with
`Host=h1,h2,h3` and `Target Session Attributes`, but it cannot tell a read from a write the way an
attribute or a scope does. Use either style, or both.

**Oracle — an Active Data Guard standby is read-only** and works as a replica. Note that Oracle's EF
Core provider ships **no `EnableRetryOnFailure`**, unlike the other three, so transparent recovery
from a warm-pool outage needs an execution strategy of your own;
`tests/Appouse.EFCore.ReplicaManager.IntegrationTests/OracleCluster.cs` has a working one to copy.

**SQLite has no replication.** Useful for tests — this repository's own suite routes between two real
SQLite files — but not a production topology.

**Every connection string gets its own ADO.NET pool.** A master plus three replicas is four pools,
each with its own `Max Pool Size`. And the model is built from the master connection string, so
replicas must expose the same schema.

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

## Options

| Option | Default | Meaning |
|---|---|---|
| `MasterConnectionString` | *(required)* | The single master. |
| `ReplicaConnectionString` | *(required)* | First replica. Set it equal to the master to disable splitting. |
| `ReplicaConnectionStrings` | empty | Additional replicas, load-balanced and failed over. |
| `DefaultTarget` | `Master` | Used when no rule applies. |
| `RouteByHttpMethod` | **`false`** | Apply the HTTP verb convention. Off, so adding the package moves no traffic. |
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

## Extension points

```csharp
// Pick replicas by latency, health or locality instead of round-robin.
services.Replace(ServiceDescriptor.Singleton<IReplicaSelector, MyReplicaSelector>());

// Track replica availability your own way.
services.Replace(ServiceDescriptor.Singleton<IReplicaHealthMonitor, MyHealthMonitor>());

// Source connection strings from a tenant catalogue or a secret store.
services.Replace(ServiceDescriptor.Singleton<IDbConnectionStringResolver, MyResolver>());
```

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
* Connection strings are never written to the log, at any level.

---

## License

MIT. See [LICENSE](https://github.com/appouse/Appouse.EFCore.ReplicaManager/blob/main/LICENSE).
