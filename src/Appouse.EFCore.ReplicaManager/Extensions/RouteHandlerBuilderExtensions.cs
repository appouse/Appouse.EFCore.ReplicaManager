using System;
using Appouse.EFCore.ReplicaManager;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Per-endpoint routing helpers for Minimal APIs, usable on a single endpoint or on a whole
/// route group.
/// </summary>
/// <remarks>
/// Each helper does two things: it attaches the corresponding metadata (so the middleware,
/// <see cref="DbTargetEndpointFilter"/> and any diagnostics all agree), and it attaches the endpoint
/// filter, so the endpoint is routed correctly even when
/// <c>app.UseDbTargetRouting()</c> is not used.
/// </remarks>
public static class ReplicaManagerEndpointConventionBuilderExtensions
{
    /// <summary>
    /// Routes the endpoint (or every endpoint in the group) to the primary (master) database.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint or route group being configured.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// app.MapGet("/accounts/{id:guid}/balance", GetBalance).UseWriteDb();
    /// </code>
    /// </example>
    public static TBuilder UseWriteDb<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => Apply(builder, new UseWriteDbAttribute());

    /// <summary>
    /// Routes the endpoint (or every endpoint in the group) to a read replica.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint or route group being configured.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// var reports = app.MapGroup("/reports").UseReadDb();
    /// </code>
    /// </example>
    public static TBuilder UseReadDb<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => Apply(builder, new UseReadDbAttribute());

    /// <summary>
    /// Routes the endpoint (or every endpoint in the group) by the HTTP verb convention, without
    /// pinning it to a specific database.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint or route group being configured.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Use this when you want Minimal API routing without adding
    /// <c>app.UseDbTargetRouting()</c> to the pipeline.
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
