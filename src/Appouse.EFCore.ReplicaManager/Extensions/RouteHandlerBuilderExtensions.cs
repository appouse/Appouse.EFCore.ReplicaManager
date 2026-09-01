using System;
using Appouse.EFCore.ReplicaManager;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Per-endpoint routing helpers for Minimal APIs, usable on a single endpoint or on a whole route
/// group.
/// <para>
/// TR: Minimal API'ler için endpoint bazlı yönlendirme yardımcıları; tek bir endpoint'te veya tüm bir
/// route grubunda kullanılabilir.
/// </para>
/// </summary>
/// <remarks>
/// Each helper does two things: it attaches the corresponding metadata, so the middleware,
/// <see cref="DbTargetEndpointFilter"/> and any diagnostics all agree; and it attaches the endpoint
/// filter, so the endpoint is routed correctly even when <c>app.UseDbTargetRouting()</c> is not used.
/// <para>
/// TR: Her yardımcı iki iş yapar: ilgili metadata'yı ekler - böylece middleware,
/// <see cref="DbTargetEndpointFilter"/> ve tüm tanılama araçları aynı sonuca varır - ve endpoint
/// filtresini ekler; böylece <c>app.UseDbTargetRouting()</c> kullanılmasa bile endpoint doğru
/// yönlendirilir.
/// </para>
/// </remarks>
public static class ReplicaManagerEndpointConventionBuilderExtensions
{
    /// <summary>
    /// Routes the endpoint, or every endpoint in the group, to the master database.
    /// <para>
    /// TR: Endpoint'i - veya gruptaki tüm endpoint'leri - master veritabanına yönlendirir.
    /// </para>
    /// </summary>
    /// <typeparam name="TBuilder">
    /// The endpoint convention builder type.
    /// <para>TR: Endpoint convention builder türü.</para>
    /// </typeparam>
    /// <param name="builder">
    /// The endpoint or route group being configured.
    /// <para>TR: Yapılandırılan endpoint veya route grubu.</para>
    /// </param>
    /// <returns>
    /// The same <paramref name="builder"/>, for chaining.
    /// <para>TR: Zincirleme için aynı <paramref name="builder"/>.</para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> is <see langword="null"/>.
    /// <para>TR: <paramref name="builder"/> <see langword="null"/>.</para>
    /// </exception>
    /// <example>
    /// <code>
    /// app.MapGet("/accounts/{id:guid}/balance", GetBalance).UseMasterDb();
    /// </code>
    /// </example>
    public static TBuilder UseMasterDb<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => Apply(builder, new UseMasterDbAttribute());

    /// <summary>
    /// Routes the endpoint, or every endpoint in the group, to a read replica.
    /// <para>
    /// TR: Endpoint'i - veya gruptaki tüm endpoint'leri - bir okuma replica'sına yönlendirir.
    /// </para>
    /// </summary>
    /// <typeparam name="TBuilder">
    /// The endpoint convention builder type.
    /// <para>TR: Endpoint convention builder türü.</para>
    /// </typeparam>
    /// <param name="builder">
    /// The endpoint or route group being configured.
    /// <para>TR: Yapılandırılan endpoint veya route grubu.</para>
    /// </param>
    /// <returns>
    /// The same <paramref name="builder"/>, for chaining.
    /// <para>TR: Zincirleme için aynı <paramref name="builder"/>.</para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> is <see langword="null"/>.
    /// <para>TR: <paramref name="builder"/> <see langword="null"/>.</para>
    /// </exception>
    /// <example>
    /// <code>
    /// var reports = app.MapGroup("/reports").UseReplicaDb();
    /// </code>
    /// </example>
    public static TBuilder UseReplicaDb<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => Apply(builder, new UseReplicaDbAttribute());

    /// <summary>
    /// Routes the endpoint, or every endpoint in the group, by the HTTP verb convention, without
    /// pinning it to a specific database.
    /// <para>
    /// TR: Endpoint'i - veya gruptaki tüm endpoint'leri - belirli bir veritabanına sabitlemeden HTTP
    /// metodu konvansiyonuna göre yönlendirir.
    /// </para>
    /// </summary>
    /// <typeparam name="TBuilder">
    /// The endpoint convention builder type.
    /// <para>TR: Endpoint convention builder türü.</para>
    /// </typeparam>
    /// <param name="builder">
    /// The endpoint or route group being configured.
    /// <para>TR: Yapılandırılan endpoint veya route grubu.</para>
    /// </param>
    /// <returns>
    /// The same <paramref name="builder"/>, for chaining.
    /// <para>TR: Zincirleme için aynı <paramref name="builder"/>.</para>
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> is <see langword="null"/>.
    /// <para>TR: <paramref name="builder"/> <see langword="null"/>.</para>
    /// </exception>
    /// <remarks>
    /// Use this when you want Minimal API routing without adding <c>app.UseDbTargetRouting()</c> to the
    /// pipeline.
    /// <para>
    /// TR: İstek hattına <c>app.UseDbTargetRouting()</c> eklemeden Minimal API yönlendirmesi istiyorsanız
    /// bunu kullanın.
    /// </para>
    /// </remarks>
    public static TBuilder UseDbTargetRouting<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddEndpointFilter<TBuilder, DbTargetEndpointFilter>();
    }

    private static TBuilder Apply<TBuilder>(TBuilder builder, IDbTargetMetadata metadata)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WithMetadata(metadata);
        return builder.AddEndpointFilter<TBuilder, DbTargetEndpointFilter>();
    }
}
