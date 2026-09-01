using System;
using Appouse.EFCore.ReplicaManager;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// Wires master/replica splitting into a <see cref="DbContextOptionsBuilder"/> you configure
/// yourself, for applications that do not use
/// <c>AddMasterReplicaDbContext&lt;TContext&gt;</c>.
/// <para>
/// TR: <c>AddMasterReplicaDbContext&lt;TContext&gt;</c> kullanmayan uygulamalar için, kendi
/// yapılandırdığınız bir <see cref="DbContextOptionsBuilder"/> üzerine master/replica ayrımını bağlar.
/// </para>
/// </summary>
public static class ReplicaManagerDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Adds the interceptors that route each connection and keep writes on the master.
    /// <para>
    /// TR: Her bağlantıyı yönlendiren ve yazmaları master'da tutan interceptor'ları ekler.
    /// </para>
    /// </summary>
    /// <param name="builder">
    /// The options builder being configured.
    /// <para>TR: Yapılandırılan options builder.</para>
    /// </param>
    /// <param name="serviceProvider">
    /// The provider passed to the <c>AddDbContext((sp, options) =&gt; ...)</c> callback. The
    /// interceptors it resolves are singletons, which is what keeps EF Core's internal service-provider
    /// cache stable.
    /// <para>
    /// TR: <c>AddDbContext((sp, options) =&gt; ...)</c> geri çağrısına verilen sağlayıcı. Çözümlediği
    /// interceptor'lar singleton'dır; EF Core'un iç servis sağlayıcı önbelleğini kararlı tutan da budur.
    /// </para>
    /// </param>
    /// <returns>
    /// The same <paramref name="builder"/>, for chaining.
    /// <para>TR: Zincirleme için aynı <paramref name="builder"/>.</para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Any argument is <see langword="null"/>.
    /// <para>TR: Argümanlardan biri <see langword="null"/>.</para>
    /// </exception>
    /// <example>
    /// <code>
    /// builder.Services.AddDbContext&lt;AppDbContext&gt;((sp, options) =&gt;
    /// {
    ///     options.UseNpgsql(masterConnectionString);
    ///     options.UseMasterReplicaSplitting(sp);
    /// });
    /// </code>
    /// </example>
    public static DbContextOptionsBuilder UseMasterReplicaSplitting(
        this DbContextOptionsBuilder builder,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        return builder.AddInterceptors(
            serviceProvider.GetRequiredService<MasterReplicaDbInterceptor>(),
            serviceProvider.GetRequiredService<MasterStickinessSaveChangesInterceptor>());
    }

    /// <summary>
    /// Strongly typed counterpart of
    /// <see cref="UseMasterReplicaSplitting(DbContextOptionsBuilder,IServiceProvider)"/>.
    /// <para>
    /// TR: <see cref="UseMasterReplicaSplitting(DbContextOptionsBuilder,IServiceProvider)"/> metodunun
    /// türü belirli karşılığı.
    /// </para>
    /// </summary>
    /// <typeparam name="TContext">
    /// The context type being configured.
    /// <para>TR: Yapılandırılan context türü.</para>
    /// </typeparam>
    /// <param name="builder">
    /// The options builder being configured.
    /// <para>TR: Yapılandırılan options builder.</para>
    /// </param>
    /// <param name="serviceProvider">
    /// The provider passed to the <c>AddDbContext</c> callback.
    /// <para>TR: <c>AddDbContext</c> geri çağrısına verilen sağlayıcı.</para>
    /// </param>
    /// <returns>
    /// The same <paramref name="builder"/>, for chaining.
    /// <para>TR: Zincirleme için aynı <paramref name="builder"/>.</para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Any argument is <see langword="null"/>.
    /// <para>TR: Argümanlardan biri <see langword="null"/>.</para>
    /// </exception>
    public static DbContextOptionsBuilder<TContext> UseMasterReplicaSplitting<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        IServiceProvider serviceProvider)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        UseMasterReplicaSplitting((DbContextOptionsBuilder)builder, serviceProvider);
        return builder;
    }
}
