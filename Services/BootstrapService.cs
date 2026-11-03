using Dapper;
using Gladi_Bersih.Common;
using Gladi_Bersih.DTOs;
using Gladi_Bersih.Infrastructure;
using Gladi_Bersih.Models;

namespace Gladi_Bersih.Services;

public sealed class BootstrapService : IBootstrapService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IActivityLogService _activityLog;

    public BootstrapService(
        IDbConnectionFactory connectionFactory,
        IActivityLogService activityLog)
    {
        _connectionFactory = connectionFactory;
        _activityLog = activityLog;
    }

    public async Task<UserResponse> CreateFirstAdminAsync(
        BootstrapAdminRequest request,
        CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        var name = request.Nama.Trim();
        var phone = PhoneNumberNormalizer.Normalize(request.NoTelepon);
        AuthService.ValidateIdentityFields(username, name);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            const string adminCountSql = """
                SELECT COUNT(*)
                FROM users u
                INNER JOIN roles r ON r.id = u.id_role
                WHERE r.nama = 'Admin';
                """;
            var countCommand = new CommandDefinition(
                adminCountSql,
                transaction: transaction,
                cancellationToken: cancellationToken);
            var adminCount = await connection.ExecuteScalarAsync<long>(countCommand);
            if (adminCount > 0)
            {
                throw new AppException(StatusCodes.Status409Conflict, "Admin pertama sudah pernah dibuat.");
            }

            await AuthService.EnsureUniqueAsync(
                connection,
                username,
                phone,
                transaction,
                cancellationToken);

            const string insertSql = """
                INSERT INTO users (id_role, username, nama, password, no_telepon, alamat)
                SELECT id, @Username, @Nama, @PasswordHash, @NoTelepon, @Alamat
                FROM roles
                WHERE nama = 'Admin';

                SELECT LAST_INSERT_ID();
                """;
            var insertCommand = new CommandDefinition(
                insertSql,
                new
                {
                    Username = username,
                    Nama = name,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, 12),
                    NoTelepon = phone,
                    Alamat = AuthService.NormalizeOptional(request.Alamat)
                },
                transaction,
                cancellationToken: cancellationToken);
            var id = checked((uint)await connection.QuerySingleAsync<ulong>(insertCommand));

            const string selectSql = """
                SELECT
                    u.id,
                    u.id_role AS IdRole,
                    u.username,
                    u.nama,
                    u.no_telepon AS NoTelepon,
                    u.alamat,
                    r.nama AS Role,
                    u.created_at AS CreatedAt
                FROM users u
                INNER JOIN roles r ON r.id = u.id_role
                WHERE u.id = @Id
                LIMIT 1;
                """;
            var selectCommand = new CommandDefinition(
                selectSql,
                new { Id = id },
                transaction,
                cancellationToken: cancellationToken);
            var user = await connection.QuerySingleAsync<UserModel>(selectCommand);
            await _activityLog.WriteAsync(
                id,
                $"Bootstrap Admin pertama '{username}'.",
                transaction,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AuthService.ToResponse(user);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
