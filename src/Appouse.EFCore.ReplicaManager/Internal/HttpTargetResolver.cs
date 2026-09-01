using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Appouse.EFCore.ReplicaManager.Internal;

/// <summary>
/// Shared routing rules for every HTTP entry point - MVC actions, Razor Page handlers, Minimal API
/// endpoints and the catch-all middleware - so all four agree on the answer.
/// <para>
/// TR: Tüm HTTP giriş noktaları - MVC action'ları, Razor Page handler'ları, Minimal API endpoint'leri
/// ve her şeyi kapsayan middleware - için ortak yönlendirme kuralları; böylece dördü de aynı sonuca
/// varır.
/// </para>
/// </summary>
internal static class HttpTargetResolver
{
    /// <summary>
    /// Reads an explicit target from endpoint routing metadata.
    /// <para>TR: Endpoint yönlendirme metadata'sından açık bir hedef okur.</para>
    /// </summary>
    /// <param name="metadata">
    /// The endpoint's metadata collection, if any.
    /// <para>TR: Varsa endpoint'in metadata koleksiyonu.</para>
    /// </param>
    /// <returns>
    /// The pinned target, or <see langword="null"/> when the endpoint is unannotated.
    /// <para>TR: Sabitlenmiş hedef; endpoint işaretlenmemişse <see langword="null"/>.</para>
    /// </returns>
    /// <remarks>
    /// <see cref="EndpointMetadataCollection.GetMetadata{T}"/> returns the <em>last</em> matching item,
    /// and ASP.NET Core appends action-level metadata after controller-level metadata, so an attribute
    /// on the action or endpoint automatically beats one on the controller.
    /// <para>
    /// TR: <see cref="EndpointMetadataCollection.GetMetadata{T}"/> eşleşen <em>son</em> öğeyi döndürür
    /// ve ASP.NET Core action seviyesindeki metadata'yı controller seviyesindekinden sonra ekler;
    /// böylece action veya endpoint üzerindeki attribute, controller üzerindekini kendiliğinden yener.
    /// </para>
    /// </remarks>
    internal static DbTarget? FromMetadata(EndpointMetadataCollection? metadata)
        => metadata?.GetMetadata<IDbTargetMetadata>()?.Target;

    /// <summary>
    /// Reads an explicit target from an MVC or Razor Pages descriptor's metadata list.
    /// <para>TR: Bir MVC veya Razor Pages tanımlayıcısının metadata listesinden açık hedef okur.</para>
    /// </summary>
    /// <param name="metadata">
    /// <c>ActionDescriptor.EndpointMetadata</c>, ordered controller-first then action.
    /// <para>
    /// TR: <c>ActionDescriptor.EndpointMetadata</c>; önce controller, sonra action sırasıyla.
    /// </para>
    /// </param>
    /// <returns>
    /// The pinned target, or <see langword="null"/> when nothing is annotated.
    /// <para>TR: Sabitlenmiş hedef; hiçbir şey işaretlenmemişse <see langword="null"/>.</para>
    /// </returns>
    /// <remarks>
    /// Scanned back to front so the most specific annotation - the one on the action - wins, matching
    /// <see cref="FromMetadata(EndpointMetadataCollection)"/>.
    /// <para>
    /// TR: En özel işaret - action üzerindeki - kazansın diye sondan başa taranır; bu,
    /// <see cref="FromMetadata(EndpointMetadataCollection)"/> ile aynı davranışı verir.
    /// </para>
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
    /// <para>
    /// TR: HTTP metodu konvansiyonunu uygular: yan etkisiz, güvenli metotlar replica'dan okur; diğer
    /// her şey master'a yazar.
    /// </para>
    /// </summary>
    /// <param name="httpMethod">
    /// The request's HTTP method.
    /// <para>TR: İsteğin HTTP metodu.</para>
    /// </param>
    /// <returns>
    /// The conventional target for the verb.
    /// <para>TR: Metoda karşılık gelen alışılmış hedef.</para>
    /// </returns>
    /// <remarks>
    /// <c>GET</c>, <c>HEAD</c>, <c>OPTIONS</c> and <c>TRACE</c> are the verbs RFC 9110 defines as safe.
    /// Every other verb - including one this package has never heard of - falls back to the master,
    /// because guessing wrong towards a replica breaks writes whereas guessing wrong towards the
    /// master merely costs a little capacity.
    /// <para>
    /// TR: <c>GET</c>, <c>HEAD</c>, <c>OPTIONS</c> ve <c>TRACE</c>, RFC 9110'un güvenli saydığı
    /// metotlardır. Diğer tüm metotlar - bu paketin hiç duymadıkları dahil - master'a düşer; çünkü
    /// replica yönünde yanlış tahmin yazmaları bozar, master yönünde yanlış tahmin ise yalnızca biraz
    /// kapasiteye mal olur.
    /// </para>
    /// </remarks>
    internal static DbTarget FromHttpMethod(string? httpMethod)
    {
        if (httpMethod is null)
        {
            return DbTarget.Master;
        }

        return HttpMethods.IsGet(httpMethod)
               || HttpMethods.IsHead(httpMethod)
               || HttpMethods.IsOptions(httpMethod)
               || HttpMethods.IsTrace(httpMethod)
            ? DbTarget.Replica
            : DbTarget.Master;
    }

    /// <summary>
    /// Resolves the effective target for a request, in precedence order.
    /// <para>TR: Bir istek için geçerli hedefi öncelik sırasına göre çözümler.</para>
    /// </summary>
    /// <param name="explicitTarget">
    /// A target pinned by an attribute, if any.
    /// <para>TR: Varsa bir attribute tarafından sabitlenmiş hedef.</para>
    /// </param>
    /// <param name="httpMethod">
    /// The request's HTTP method.
    /// <para>TR: İsteğin HTTP metodu.</para>
    /// </param>
    /// <param name="options">
    /// The configured options.
    /// <para>TR: Yapılandırılmış ayarlar.</para>
    /// </param>
    /// <param name="ambientTarget">
    /// The target currently in effect, used as the last resort.
    /// <para>TR: Şu an geçerli olan hedef; son çare olarak kullanılır.</para>
    /// </param>
    /// <returns>
    /// The target the request must run against.
    /// <para>TR: İsteğin çalışacağı hedef.</para>
    /// </returns>
    internal static DbTarget Resolve(
        DbTarget? explicitTarget,
        string? httpMethod,
        MasterReplicaOptions options,
        DbTarget ambientTarget)
    {
        if (explicitTarget.HasValue)
        {
            return explicitTarget.Value;
        }

        return options.RouteByHttpMethod ? FromHttpMethod(httpMethod) : ambientTarget;
    }
}
