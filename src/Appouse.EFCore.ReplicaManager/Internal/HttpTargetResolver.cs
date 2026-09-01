using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Appouse.EFCore.ReplicaManager.Internal;

/// <summary>
/// Shared routing rules for every HTTP entry point - MVC actions, Razor Page handlers, Minimal API
/// endpoints and the catch-all middleware - so that all four agree on the answer.
/// </summary>
internal static class HttpTargetResolver
{
    /// <summary>
    /// Reads an explicit target from Minimal API / endpoint routing metadata.
    /// </summary>
    /// <param name="metadata">The endpoint's metadata collection, if any.</param>
    /// <returns>The pinned target, or <see langword="null"/> when the endpoint is unannotated.</returns>
    /// <remarks>
    /// <see cref="EndpointMetadataCollection.GetMetadata{T}"/> returns the <em>last</em> matching
    /// item, and ASP.NET Core appends action-level metadata after controller-level metadata, so an
    /// attribute on the action or the endpoint automatically beats one on the controller.
    /// </remarks>
    internal static DbTarget? FromMetadata(EndpointMetadataCollection? metadata)
        => metadata?.GetMetadata<IDbTargetMetadata>()?.Target;

    /// <summary>
    /// Reads an explicit target from an MVC/Razor Pages descriptor's metadata list.
    /// </summary>
    /// <param name="metadata">
    /// <c>ActionDescriptor.EndpointMetadata</c>, ordered controller-first then action.
    /// </param>
    /// <returns>The pinned target, or <see langword="null"/> when nothing is annotated.</returns>
    /// <remarks>
    /// Scanned back to front so that the most specific annotation - the one on the action - wins,
    /// matching <see cref="FromMetadata(EndpointMetadataCollection)"/>.
    /// </remarks>
    internal static DbTarget? FromMetadata(IList<object>? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        for (var i = metadata.Count - 1; i >= 0; i--)
        {
            if (metadata[i] is IDbTargetMetadata target)
            {
                return target.Target;
            }
        }

        return null;
    }

    /// <summary>
    /// Applies the HTTP verb convention: safe, side-effect-free verbs read from a replica and
    /// everything else writes to the master.
    /// </summary>
    /// <param name="httpMethod">The request's HTTP method.</param>
    /// <returns>The conventional target for the verb.</returns>
    /// <remarks>
    /// <c>GET</c>, <c>HEAD</c>, <c>OPTIONS</c> and <c>TRACE</c> are the verbs RFC 9110 defines as
    /// safe. Every other verb - including one this package has never heard of - falls back to the
    /// master, because guessing wrong towards a replica breaks writes whereas guessing wrong
    /// towards the master merely costs a little capacity.
    /// </remarks>
    internal static DbTarget FromHttpMethod(string? httpMethod)
    {
        if (httpMethod is null)
        {
            return DbTarget.WriteMaster;
        }

        return HttpMethods.IsGet(httpMethod)
               || HttpMethods.IsHead(httpMethod)
               || HttpMethods.IsOptions(httpMethod)
               || HttpMethods.IsTrace(httpMethod)
            ? DbTarget.ReadReplica
            : DbTarget.WriteMaster;
    }

    /// <summary>
    /// Resolves the effective target for a request, in precedence order.
    /// </summary>
    /// <param name="explicitTarget">A target pinned by an attribute, if any.</param>
    /// <param name="httpMethod">The request's HTTP method.</param>
    /// <param name="options">The configured options.</param>
    /// <param name="ambientTarget">The target currently in effect, used as the last resort.</param>
    /// <returns>The target the request must run against.</returns>
    internal static DbTarget Resolve(
        DbTarget? explicitTarget,
        string? httpMethod,
        ReadWriteOptions options,
        DbTarget ambientTarget)
    {
        if (explicitTarget.HasValue)
        {
            return explicitTarget.Value;
        }

        return options.RouteByHttpMethod ? FromHttpMethod(httpMethod) : ambientTarget;
    }
}
