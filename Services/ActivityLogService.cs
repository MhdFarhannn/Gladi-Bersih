using System.Data;
using Dapper;
using Gladi_Bersih.Infrastructure;

namespace Gladi_Bersih.Services;

public sealed class ActivityLogService : IActivityLogService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ActivityLogService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task WriteAsync(
        uint? userId,
        string activity,
        IDbTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            INSERT INTO log_aktivitas (id_user, aktivitas)
            VALUES (@UserId, @Activity);
            """;

        var command = new CommandDefinition(
            sql,
            new { UserId = userId, Activity = activity },
            transaction,
            cancellationToken: cancellationToken);

        if (transaction?.Connection is not null)
        {
            await transaction.Connection.ExecuteAsync(command);
            return;
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(command);
    }
}
