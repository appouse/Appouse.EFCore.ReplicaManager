using Microsoft.EntityFrameworkCore;

namespace Sample.WebApi;

public sealed class Order
{
    public int Id { get; set; }

    public string Customer { get; set; } = string.Empty;

    public decimal Total { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}
