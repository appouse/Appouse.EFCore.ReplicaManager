using Microsoft.EntityFrameworkCore;

namespace Sample.Worker;

public sealed class Order
{
    public int Id { get; set; }

    public string Customer { get; set; } = string.Empty;

    public decimal Total { get; set; }

    public bool Settled { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}
