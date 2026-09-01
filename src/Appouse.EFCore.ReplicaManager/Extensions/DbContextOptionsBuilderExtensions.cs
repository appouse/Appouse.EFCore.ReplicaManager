using System;
using Appouse.EFCore.ReplicaManager;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Wires read/write splitting into a <see cref="DbContextOptionsBuilder"/> that you configure
/// yourself, for applications that do not use
/// <c>AddReadWriteDbContext&lt;TContext&gt;</c>.
/// </summary>
public static class ReplicaManagerDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Adds the interceptors that rewrite the connection string per operation and keep writes on
    /// the master.
    /// </summary>
    /// <param name="builder">The options builder being configured.</param>
    /// <param name="serviceProvider">
    /// The provider passed to the <c>AddDbContext((sp, options) =&gt; ...)</c> callback. The
    /// interceptors it resolves are singletons, which is what keeps EF Core's internal
    /// service-provider cache stable.
    /// </param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// builder.Services.AddDbContext&lt;AppDbContext&gt;((sp, options) =&gt;
    /// {
    ///     options.UseNpgsql(masterConnectionString);
    ///     options.UseReadWriteSplitting(sp);
    /// });
    /// </code>
    /// </example>
    public static DbContextOptionsBuilder UseReadWriteSplitting(
        this DbContextOptionsBuilder builder,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        return builder.AddInterceptors(
            serviceProvider.GetRequiredService<ReadWriteDbInterceptor>(),
            serviceProvider.GetRequiredService<WriteStickinessSaveChangesInterceptor>());
    }

    /// <summary>
    /// Strongly typed counterpart of
    /// <see cref="UseReadWriteSplitting(DbContextOptionsBuilder,IServiceProvider)"/>.
    /// </summary>
    /// <typeparam name="TContext">The context type being configured.</typeparam>
    /// <param name="builder">The options builder being configured.</param>
    /// <param name="serviceProvider">The provider passed to the <c>AddDbContext</c> callback.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public static DbContextOptionsBuilder<TContext> UseReadWriteSplitting<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        IServiceProvider serviceProvider)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        UseReadWriteSplitting((DbContextOptionsBuilder)builder, serviceProvider);
        return builder;
    }
}
