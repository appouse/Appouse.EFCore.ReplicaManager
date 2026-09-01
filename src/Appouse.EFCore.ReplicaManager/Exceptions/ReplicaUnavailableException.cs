using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// Thrown when every configured replica refused a connection and
/// <see cref="MasterReplicaOptions.AllowReplicaFallbackToMaster"/> is disabled, so the read cannot be
/// served at all.
/// <para>
/// TR: Tanımlı tüm replica'lar bağlantıyı reddettiğinde ve
/// <see cref="MasterReplicaOptions.AllowReplicaFallbackToMaster"/> kapalıyken fırlatılır; bu durumda
/// okuma hiçbir şekilde karşılanamaz.
/// </para>
/// </summary>
/// <remarks>
/// The individual provider failures are preserved in
/// <see cref="Exception.InnerException"/> as an <see cref="AggregateException"/>, one entry per
/// replica in the order they were attempted.
/// <para>
/// TR: Sağlayıcının bildirdiği tekil hatalar, denendikleri sırayla replica başına bir kayıt olacak
/// şekilde <see cref="Exception.InnerException"/> içinde bir <see cref="AggregateException"/> olarak
/// korunur.
/// </para>
/// </remarks>
public sealed class ReplicaUnavailableException : Exception
{
    /// <summary>
    /// Creates an exception with the default message.
    /// <para>TR: Varsayılan mesajla bir istisna oluşturur.</para>
    /// </summary>
    public ReplicaUnavailableException()
        : base("No read replica could be reached.")
    {
    }

    /// <summary>
    /// Creates an exception with the supplied message.
    /// <para>TR: Verilen mesajla bir istisna oluşturur.</para>
    /// </summary>
    /// <param name="message">
    /// The message describing the failure.
    /// <para>TR: Hatayı açıklayan mesaj.</para>
    /// </param>
    public ReplicaUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an exception with the supplied message and cause.
    /// <para>TR: Verilen mesaj ve nedenle bir istisna oluşturur.</para>
    /// </summary>
    /// <param name="message">
    /// The message describing the failure.
    /// <para>TR: Hatayı açıklayan mesaj.</para>
    /// </param>
    /// <param name="innerException">
    /// The underlying cause.
    /// <para>TR: Altta yatan neden.</para>
    /// </param>
    public ReplicaUnavailableException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates an exception describing how many replicas were tried and why each one failed.
    /// <para>
    /// TR: Kaç replica'nın denendiğini ve her birinin neden başarısız olduğunu açıklayan bir istisna
    /// oluşturur.
    /// </para>
    /// </summary>
    /// <param name="attemptedReplicaCount">
    /// How many replicas were dialled.
    /// <para>TR: Kaç replica'ya bağlanılmaya çalışıldı.</para>
    /// </param>
    /// <param name="failures">
    /// The failure reported by each attempt, in order.
    /// <para>TR: Her denemenin bildirdiği hata, sırasıyla.</para>
    /// </param>
    /// <returns>
    /// An exception ready to throw.
    /// <para>TR: Fırlatılmaya hazır bir istisna.</para>
    /// </returns>
    internal static ReplicaUnavailableException ForFailedAttempts(
        int attemptedReplicaCount,
        IReadOnlyCollection<Exception> failures)
    {
        var message =
            $"None of the {attemptedReplicaCount} configured read replica(s) accepted a connection, and " +
            $"{nameof(MasterReplicaOptions)}.{nameof(MasterReplicaOptions.AllowReplicaFallbackToMaster)} is " +
            "disabled, so the master was not used as a fallback. Inspect the inner exceptions for the failure " +
            "reported by each replica.";

        return new ReplicaUnavailableException(message, new AggregateException(failures));
    }

    /// <summary>
    /// Creates an exception for a topology that declares no replica at all.
    /// <para>TR: Hiç replica tanımlamayan bir topoloji için istisna oluşturur.</para>
    /// </summary>
    /// <returns>
    /// An exception ready to throw.
    /// <para>TR: Fırlatılmaya hazır bir istisna.</para>
    /// </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static ReplicaUnavailableException ForMissingConfiguration()
        => new(
            $"{DbTarget.Replica} was requested but no replica connection string is configured, and " +
            $"{nameof(MasterReplicaOptions)}.{nameof(MasterReplicaOptions.AllowReplicaFallbackToMaster)} is " +
            $"disabled. Set {nameof(MasterReplicaOptions)}.{nameof(MasterReplicaOptions.ReplicaConnectionString)}, " +
            "or enable the fallback.");
}
