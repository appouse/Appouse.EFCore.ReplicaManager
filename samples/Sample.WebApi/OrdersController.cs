using Appouse.EFCore.ReplicaManager;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Sample.WebApi;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(AppDbContext db, IDbTargetContext dbTarget) : ControllerBase
{
    /// <summary>GET, so it is served from a read replica. Nothing to declare.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<Order>> List()
        => await db.Orders.OrderByDescending(o => o.CreatedAt).Take(50).ToListAsync();

    /// <summary>POST, so it goes to the master. Also nothing to declare.</summary>
    [HttpPost]
    public async Task<ActionResult<Order>> Create(Order order)
    {
        order.CreatedAt = DateTimeOffset.UtcNow;
        db.Orders.Add(order);

        // Because a write happened, everything read after this point in the request also comes
        // from the master - so the response below never shows a stale row.
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    /// <summary>A GET that must not observe replication lag: pin it to the master.</summary>
    [HttpGet("{id:int}")]
    [UseWriteDb]
    public async Task<ActionResult<Order>> Get(int id)
        => await db.Orders.FindAsync(id) is { } order ? order : NotFound();

    /// <summary>A POST that only reads: let a replica carry the load.</summary>
    [HttpPost("search")]
    [UseReadDb]
    public async Task<IReadOnlyList<Order>> Search(string customer)
        => await db.Orders.Where(o => o.Customer == customer).ToListAsync();

    /// <summary>
    /// Explicit control inside an action, for the cases attributes cannot express - here, reading
    /// a freshly written row back from the master while the rest of the action stays on a replica.
    /// </summary>
    [HttpGet("{id:int}/receipt")]
    public async Task<ActionResult<Order>> Receipt(int id)
    {
        using (dbTarget.UseWriteDb())
        {
            return await db.Orders.FindAsync(id) is { } order ? order : NotFound();
        }
    }
}
