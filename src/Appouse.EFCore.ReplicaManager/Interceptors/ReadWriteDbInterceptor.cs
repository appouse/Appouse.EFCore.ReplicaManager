using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Appouse.EFCore.ReplicaManager.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Appouse.EFCore.ReplicaManager;

/// <summary>
/// The mechanism that makes read/write splitting transparent: immediately before EF Core opens a
/// <see cref="DbConnection"/>, this interceptor rewrites
/// <see cref="DbConnection.ConnectionString"/> to the master or to a replica, according to the
/// ambient <see cref="IDbTargetContext.CurrentTarget"/>.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a <em>singleton</em>. Interceptors are captured inside <c>DbContextOptions</c>,
/// and EF Core keys its internal service-provider cache on those options: handing it a different
/// interceptor instance per scope would build a fresh internal provider per scope. Keeping the
/// interceptor stateless - all per-request state lives in <see cref="IDbTargetContext"/>'s
/// <see cref="AsyncLocal{T}"/> - is what makes the singleton lifetime correct.
/// </para>
/// <para>
/// Both the synchronous and the asynchronous open paths are intercepted, because EF Core calls
/// whichever matches the query the application issued.
/// </para>
/// <para>
/// <strong>Known limitation.</strong> A connection string can only be assigned while the connection
/// is closed. EF Core opens late and closes early, so in the common case every operation gets a
/// fresh routing decision. It does <em>not</em> close the connection between operations while an
/// explicit transaction is active or after an explicit <c>Database.OpenConnection()</c>: in those
/// cases the route is fixed at the moment the connection was opened. Start such work inside an
/// <see cref="IDbTargetContext.UseTarget"/> scope.
/// </para>
/// </remarks>
public sealed class ReadWriteDbInterceptor : DbConnectionInterceptor
{
    private readonly ILogger<ReadWriteDbInterceptor> _logger;
    private readonly ReadWriteOptions _options;
    private readonly IDbConnectionStringResolver _resolver;
    private readonly IDbTargetContext _targetContext;

    /// <summary>
    /// Creates the interceptor.
    /// </summary>
    /// <param name="targetContext">Ambient store holding the target for the current flow.</param>
    /// <param name="resolver">Translates a target into a connection string.</param>
    /// <param name="options">The configured read/write splitting options.</param>
    /// <param name="logger">Diagnostics sink. Connection strings are never written to it.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public ReadWriteDbInterceptor(
        IDbTargetContext targetContext,
        IDbConnectionStringResolver resolver,
        IOptions<ReadWriteOptions> options,
        ILogger<ReadWriteDbInterceptor> logger)
    {
        ArgumentNullException.ThrowIfNull(targetContext);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _targetContext = targetContext;
        _resolver = resolver;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        Route(connection, eventData);
        return base.ConnectionOpening(connection, eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        Route(connection, eventData);
        return base.ConnectionOpeningAsync(connection, eventData, result, cancellationToken);
    }

    private void Route(DbConnection connection, ConnectionEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(eventData);

        if (connection.State != ConnectionState.Closed)
        {
            Log.ConnectionAlreadyOpen(_logger, eventData.ConnectionId, connection.State.ToString());
            return;
        }

        var target = ResolveTarget(eventData);
        var connectionString = _resolver.Resolve(target);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Log.EmptyConnectionString(_logger, target);
            return;
        }

        if (!string.Equals(connection.ConnectionString, connectionString, StringComparison.Ordinal))
        {
            connection.ConnectionString = connectionString;
        }

        Log.ConnectionRouted(_logger, eventData.ConnectionId, target);
    }

    private DbTarget ResolveTarget(ConnectionEventData eventData)
    {
        var target = _targetContext.CurrentTarget;

        if (target == DbTarget.WriteMaster || !_options.ForceWriteInsideTransaction)
        {
            return target;
        }

        if (eventData.Context?.Database.CurrentTransaction is not null || Transaction.Current is not null)
        {
            Log.TransactionForcedWrite(_logger, eventData.ConnectionId, DbTarget.WriteMaster);
            return DbTarget.WriteMaster;
        }

        return target;
    }
}
