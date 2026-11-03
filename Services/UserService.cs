using System.Data;
using Dapper;
using Gladi_Bersih.Common;
using Gladi_Bersih.DTOs;
using Gladi_Bersih.Infrastructure;
using Gladi_Bersih.Models;

namespace Gladi_Bersih.Services;

public sealed class UserService : IUserService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IActivityLogService _activityLog;

    public UserService(IDbConnectionFactory connectionFactory, IActivityLogService activityLog)
    {
        _connectionFactory = connectionFactory;
        _activityLog = activityLog;
    }

    public async Task<PagedResult<UserResponse>> GetAllAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken)
    {
        var pagination = Pagination.Create(page, pageSize);
        var searchPattern = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        const string sql = """
            SELECT COUNT(*)
            FROM users u
            WHERE u.deleted_at IS NULL
              AND (@Search IS NULL
                   OR u.username LIKE @Search
                   OR u.nama LIKE @Search
                   OR u.no_telepon LIKE @Search);

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
            WHERE u.deleted_at IS NULL
              AND (@Search IS NULL
                   OR u.username LIKE @Search
                   OR u.nama LIKE @Search
                   OR u.no_telepon LIKE @Search)
            ORDER BY u.id DESC
            LIMIT @PageSize OFFSET @Offset;
            """;

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = new CommandDefinition(
            sql,
            new { Search = searchPattern, pagination.PageSize, pagination.Offset },
            cancellationToken: cancellationToken);
        using var result = await connection.QueryMultipleAsync(command);
        var total = await result.ReadSingleAsync<long>();
        var users = (await result.ReadAsync<UserModel>()).Select(AuthService.ToResponse).ToArray();
        return new PagedResult<UserResponse>(users, pagination.Page, pagination.PageSize, total);
    }

    public async Task<UserResponse> GetByIdAsync(uint id, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return AuthService.ToResponse(await GetActiveByIdAsync(connection, id, null, cancellationToken));
    }

    public async Task<UserResponse> CreateAsync(
        uint actorId,
        UserCreateRequest request,
        CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        var name = request.Nama.Trim();
        var phone = PhoneNumberNormalizer.Normalize(request.NoTelepon);
        var role = NormalizeCreateRole(request.Role);
        AuthService.ValidateIdentityFields(username, name);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await AuthService.EnsureUniqueAsync(
                connection,
                username,
                phone,
                transaction,
                cancellationToken);
            await EnsureRoleExistsAsync(connection, role, transaction, cancellationToken);

            const string sql = """
                INSERT INTO users (id_role, username, nama, password, no_telepon, alamat)
                VALUES (
                    (SELECT id FROM roles WHERE nama = @Role LIMIT 1),
                    @Username,
                    @Nama,
                    @PasswordHash,
                    @NoTelepon,
                    @Alamat
                );

                SELECT LAST_INSERT_ID();
                """;
            var command = new CommandDefinition(
                sql,
                new
                {
                    Role = role,
                    Username = username,
                    Nama = name,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, 12),
                    NoTelepon = phone,
                    Alamat = AuthService.NormalizeOptional(request.Alamat)
                },
                transaction,
                cancellationToken: cancellationToken);
            var id = checked((uint)await connection.QuerySingleAsync<ulong>(command));
            var user = await GetActiveByIdAsync(connection, id, transaction, cancellationToken);
            await _activityLog.WriteAsync(
                actorId,
                $"Membuat user {role} '{username}' (ID {id}).",
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

    public async Task<UserResponse> UpdateAsync(
        uint actorId,
        uint id,
        UserUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        var name = request.Nama.Trim();
        var phone = PhoneNumberNormalizer.Normalize(request.NoTelepon);
        var role = NormalizeAnyRole(request.Role);
        AuthService.ValidateIdentityFields(username, name);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await GetActiveByIdAsync(connection, id, transaction, cancellationToken, true);
            await AuthService.EnsureUniqueAsync(
                connection,
                username,
                phone,
                transaction,
                cancellationToken,
                id);
            await EnsureRoleExistsAsync(connection, role, transaction, cancellationToken);

            const string sql = """
                UPDATE users
                SET id_role = (SELECT id FROM roles WHERE nama = @Role LIMIT 1),
                    username = @Username,
                    nama = @Nama,
                    password = COALESCE(@PasswordHash, password),
                    no_telepon = @NoTelepon,
                    alamat = @Alamat
                WHERE id = @Id
                  AND deleted_at IS NULL;
                """;
            var passwordHash = string.IsNullOrWhiteSpace(request.Password)
                ? null
                : BCrypt.Net.BCrypt.HashPassword(request.Password, 12);
            var command = new CommandDefinition(
                sql,
                new
                {
                    Id = id,
                    Role = role,
                    Username = username,
                    Nama = name,
                    PasswordHash = passwordHash,
                    NoTelepon = phone,
                    Alamat = AuthService.NormalizeOptional(request.Alamat)
                },
                transaction,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(command);
            var user = await GetActiveByIdAsync(connection, id, transaction, cancellationToken);
            await _activityLog.WriteAsync(
                actorId,
                $"Memperbarui user '{username}' (ID {id}).",
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

    public async Task DeleteAsync(uint actorId, uint id, CancellationToken cancellationToken)
    {
        const string guardSql = """
            SELECT EXISTS(
                SELECT 1
                FROM peminjaman p
                INNER JOIN status_peminjaman sp ON sp.id = p.id_status_peminjaman
                WHERE (p.id_peminjam = @Id OR p.id_petugas = @Id)
                  AND p.deleted_at IS NULL
                  AND sp.nama IN ('diajukan', 'disetujui', 'dipinjam')
            );
            """;
        const string deleteSql = """
            UPDATE users
            SET deleted_at = NOW()
            WHERE id = @Id
              AND deleted_at IS NULL;
            """;

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var user = await GetActiveByIdAsync(connection, id, transaction, cancellationToken, true);
            var guardCommand = new CommandDefinition(
                guardSql,
                new { Id = id },
                transaction,
                cancellationToken: cancellationToken);
            if (await connection.ExecuteScalarAsync<bool>(guardCommand))
            {
                throw new AppException(
                    StatusCodes.Status409Conflict,
                    "User tidak dapat dihapus karena masih memiliki peminjaman aktif.");
            }

            var deleteCommand = new CommandDefinition(
                deleteSql,
                new { Id = id },
                transaction,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(deleteCommand);
            await _activityLog.WriteAsync(
                actorId,
                $"Menghapus user '{user.Username}' (ID {id}) secara soft delete.",
                transaction,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static string NormalizeCreateRole(string value)
    {
        var role = value.Trim();
        if (role.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return "Admin";
        }

        if (role.Equals("Petugas", StringComparison.OrdinalIgnoreCase))
        {
            return "Petugas";
        }

        throw new AppException(
            StatusCodes.Status400BadRequest,
            "Admin hanya dapat membuat akun dengan role Admin atau Petugas.");
    }

    private static string NormalizeAnyRole(string value)
    {
        var role = value.Trim();
        foreach (var allowedRole in new[] { "Admin", "Petugas", "Peminjam" })
        {
            if (role.Equals(allowedRole, StringComparison.OrdinalIgnoreCase))
            {
                return allowedRole;
            }
        }

        throw new AppException(StatusCodes.Status400BadRequest, "Role tidak valid.");
    }

    private static async Task EnsureRoleExistsAsync(
        IDbConnection connection,
        string role,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT EXISTS(SELECT 1 FROM roles WHERE nama = @Role);";
        var command = new CommandDefinition(sql, new { Role = role }, transaction, cancellationToken: cancellationToken);
        if (!await connection.ExecuteScalarAsync<bool>(command))
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Role tidak ditemukan di database.");
        }
    }

    private static async Task<UserModel> GetActiveByIdAsync(
        IDbConnection connection,
        uint id,
        IDbTransaction? transaction,
        CancellationToken cancellationToken,
        bool forUpdate = false)
    {
        var sql = """
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
              AND u.deleted_at IS NULL
            LIMIT 1
            """ + (forUpdate ? " FOR UPDATE;" : ";");
        var command = new CommandDefinition(sql, new { Id = id }, transaction, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<UserModel>(command)
            ?? throw new AppException(StatusCodes.Status404NotFound, "User tidak ditemukan.");
    }
}
