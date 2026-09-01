# Appouse.EFCore.ReplicaManager

Transparent Read/Write (Master/Replica) database splitting for **EF Core 8+**.

`GET` requests read from a replica, writes go to the master, and a `using` block gives you explicit
control anywhere else — including background workers, where there is no `HttpContext` to hang
routing off. Provider agnostic: SQL Server, PostgreSQL, MySQL, SQLite and anything else with an EF
Core provider.

```bash
dotnet add package Appouse.EFCore.ReplicaManager
```

---

## 30 seconds

```csharp
using Appouse.EFCore.ReplicaManager;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEfCoreReadWriteSplit(options =>
{
    options.WriteConnectionString = builder.Configuration.GetConnectionString("Master")!;
    options.ReadConnectionString  = builder.Configuration.GetConnectionString("Replica")!;
    options.DefaultTarget         = DbTarget.WriteMaster;
});

// You choose the provider; the package hands you the connection string.
builder.Services.AddReadWriteDbContext<AppDbContext>((options, connectionString) =>
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
| 1 | `[UseWriteDb]` / `[UseReadDb]` on the **action** | that target |
| 2 | `[UseWriteDb]` / `[UseReadDb]` on the **controller** | that target |
| 3 | `SaveChanges` is running | **always** `WriteMaster` |
| 4 | A transaction is active | **always** `WriteMaster` |
| 5 | An enclosing `UseTarget(...)` scope | that target |
| 6 | HTTP verb — `GET`, `HEAD`, `OPTIONS`, `TRACE` | `ReadReplica` |
| 7 | HTTP verb — everything else | `WriteMaster` |
| 8 | Nothing above applies | `options.DefaultTarget` |

Unknown verbs fall back to the master: guessing wrong towards a replica breaks writes, guessing
wrong towards the master only costs a little capacity.

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
    [UseWriteDb]                    // a GET that must not read stale data
    public Task<Order?> Get(int id) => db.Orders.FindAsync(id).AsTask();

    [HttpPost("search")]
    [UseReadDb]                     // a POST that only reads
    public Task<List<Order>> Search(SearchRequest r) { /* ... */ }
}
```

An attribute on the action always beats one on the controller.

### Minimal APIs

```csharp
app.MapGet("/orders/{id:int}", handler).UseWriteDb();
app.MapPost("/orders/report", handler).UseReadDb();

var reports = app.MapGroup("/reports").UseReadDb();   // applies to the whole group
```

> A Minimal API endpoint is only routed if you either add `app.UseDbTargetRouting()` to the
> pipeline or use one of the helpers above. Without either, it falls back to `DefaultTarget`.

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
        using (dbTarget.UseReadDb())
        {
            candidates = await db.Orders.Where(o => !o.Settled).Select(o => o.Id).ToListAsync(stoppingToken);
        }

        // Update where the rows are authoritative.
        using (dbTarget.UseWriteDb())
        {
            /* ... */
            await db.SaveChangesAsync(stoppingToken);
        }
    }
}
```

Registration is the same call as in the web app — `AddEfCoreReadWriteSplit` deliberately touches no
ASP.NET Core type, so a `Microsoft.NET.Sdk.Worker` project on a runtime-only container image builds
and runs with no ASP.NET Core shared framework installed.

---

## Replication lag: read your own writes

The single most common way read/write splitting breaks an application is a write followed
immediately by a read that lands on a replica which has not caught up.

Two mechanisms handle this, both on by default:

* **`ForceWriteOnSaveChanges`** — `SaveChanges` switches to the master *before* touching the
  database. A `GET` action that stamps `LastSeenAt` or writes an audit row therefore works, instead
  of failing against a read-only replica.
* **`StickToWriteAfterSaveChanges`** — after a successful save, the rest of the scope stays on the
  master. Within a request, everything read after a write is read back from the master.

Beyond the current scope the package **cannot** detect lag; nothing can, from inside the
application. If a later request must observe an earlier write, pin it explicitly:

```csharp
using (dbTarget.UseWriteDb())
{
    var order = await db.Orders.FindAsync(id);
}
```

---

## Migrations

`WriteConnectionString` is the canonical connection string: it is what the provider gets for model
building, what `dotnet ef` uses, and what `Database.Migrate()` connects to. Keep `DefaultTarget` at
`WriteMaster`, or wrap migration explicitly:

```csharp
using (dbTarget.UseWriteDb())
{
    await db.Database.MigrateAsync();
}
```

---

## Options

| Option | Default | Meaning |
|---|---|---|
| `WriteConnectionString` | *(required)* | Master connection string. |
| `ReadConnectionString` | *(required)* | Primary replica. Set it equal to the master to disable splitting. |
| `ReadConnectionStrings` | empty | Additional replicas, load-balanced round-robin. |
| `DefaultTarget` | `WriteMaster` | Used when no rule applies. |
| `RouteByHttpMethod` | `true` | Apply the verb convention. |
| `ForceWriteInsideTransaction` | `true` | Transactions always use the master. |
| `ForceWriteOnSaveChanges` | `true` | `SaveChanges` always uses the master. |
| `StickToWriteAfterSaveChanges` | `true` | Read-after-write consistency within the scope. |
| `AllowReadFallbackToWrite` | `true` | Use the master when no replica is configured. |
| `MvcActionFilterOrder` | `int.MinValue` | Filter position; lowest runs first. |

Bind from configuration instead, if you prefer:

```csharp
builder.Services.AddEfCoreReadWriteSplit(
    builder.Configuration.GetSection(ReadWriteOptions.SectionName));   // "EfCoreReadWriteSplit"
```

Configuration is validated at host start-up, so a missing connection string stops the application
instead of surfacing as a failure on the first query.

---

## Extension points

```csharp
// Pick replicas by latency, health or weight instead of round-robin.
services.Replace(ServiceDescriptor.Singleton<IReadReplicaSelector, MyReplicaSelector>());

// Source connection strings from a tenant catalogue or a secret store.
services.Replace(ServiceDescriptor.Singleton<IDbConnectionStringResolver, MyResolver>());
```

Already registering the `DbContext` yourself? Wire the interceptors in directly:

```csharp
services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseNpgsql(masterConnectionString);
    options.UseReadWriteSplitting(sp);
});
```

---

## How it works

A singleton `DbConnectionInterceptor` rewrites `DbConnection.ConnectionString` in
`ConnectionOpening` / `ConnectionOpeningAsync`, immediately before EF Core opens the connection.

Two design decisions are load-bearing:

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

### Limitations

* A connection string can only be assigned while the connection is **closed**. EF Core opens late
  and closes early, so each operation is routed independently — but not while an explicit
  transaction is open, or after `Database.OpenConnection()`. Start that work inside a
  `UseTarget(...)` scope; the route is fixed when the connection opens.
* Deferred execution follows the target in effect when the query *runs*, not when it is composed.
  `IQueryable` built inside a scope and enumerated outside it uses the outer target.
* Each connection string gets its own ADO.NET connection pool. Size `Max Pool Size` accordingly.
* Connection strings are never written to the log, at any level.

---

## License

MIT. See [LICENSE](https://github.com/appouse/Appouse.EFCore.ReplicaManager/blob/main/LICENSE).
