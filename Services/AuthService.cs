using Dapper;
using Gladi_Bersih.Common;
using Gladi_Bersih.DTOs;
using Gladi_Bersih.Infrastructure;
using Gladi_Bersih.Models;

namespace Gladi_Bersih.Services;

public sealed class AuthService : IAuthService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IActivityLogService _activityLog;
    private readonly IJwtTokenService _jwtTokenService;

    public AuthService(
        IDbConnectionFactory connectionFactory,
        IActivityLogService activityLog,
        IJwtTokenService jwtTokenService)
    {
        _connectionFactory = connectionFactory;
        _activityLog = activityLog;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                u.id,
                u.id_role AS IdRole,
                u.username,
                u.nama,
                u.password AS PasswordHash,
                u.no_telepon AS NoTelepon,
                u.alamat,
                r.nama AS Role,
                u.created_at AS CreatedAt
            FROM users u
            INNER JOIN roles r ON r.id = u.id_role
            WHERE u.username = @Username
              AND u.deleted_at IS NULL
            LIMIT 1;
            """;

        var username = request.Username.Trim();
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = new CommandDefinition(sql, new { Username = username }, cancellationToken: cancellationToken);
        var user = await connection.QuerySingleOrDefaultAsync<UserModel>(command);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            await _activityLog.WriteAsync(
                null,
                $"Login gagal untuk username '{username}'.",
                cancellationToken: cancellationToken);
            throw new AppException(StatusCodes.Status401Unauthorized, "Username atau password salah");
        }

        await _activityLog.WriteAsync(
            user.Id,
            $"Login berhasil untuk username '{user.Username}'.",
            cancellationToken: cancellationToken);
        return _jwtTokenService.CreateToken(user);
    }

    public async Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        var phone = PhoneNumberNormalizer.Normalize(request.NoTelepon);
        var name = request.Nama.Trim();

        ValidateIdentityFields(username, name);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await EnsureUniqueAsync(connection, username, phone, transaction, cancellationToken);

            const string insertSql = """
                INSERT INTO users (id_role, username, nama, password, no_telepon, alamat)
                SELECT id, @Username, @Nama, @PasswordHash, @NoTelepon, @Alamat
                FROM roles
                WHERE nama = 'Peminjam';

                SELECT LAST_INSERT_ID();
                """;
            var command = new CommandDefinition(
                insertSql,
                new
                {
                    Username = username,
                    Nama = name,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, 12),
                    NoTelepon = phone,
                    Alamat = NormalizeOptional(request.Alamat)
                },
                transaction,
                cancellationToken: cancellationToken);
            var id = checked((uint)await connection.QuerySingleAsync<ulong>(command));
            var user = await GetByIdAsync(connection, id, transaction, cancellationToken);
            await _activityLog.WriteAsync(
                id,
                $"Registrasi akun Peminjam '{username}'.",
                transaction,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToResponse(user);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public Task LogoutAsync(uint userId, CancellationToken cancellationToken)
    {
        return _activityLog.WriteAsync(
            userId,
            "Logout berhasil.",
            cancellationToken: cancellationToken);
    }

    internal static UserResponse ToResponse(UserModel user)
    {
        return new UserResponse(
            user.Id,
            user.Username,
            user.Nama,
            user.NoTelepon,
            user.Alamat,
            user.Role,
            user.CreatedAt);
    }

    internal static async Task EnsureUniqueAsync(
        System.Data.IDbConnection connection,
        string username,
        string phone,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken,
        uint? excludedId = null)
    {
        const string sql = """
            SELECT
                EXISTS(
                    SELECT 1
                    FROM users
                    WHERE username = @Username
                      AND (@ExcludedId IS NULL OR id <> @ExcludedId)
                ) AS UsernameExists,
                EXISTS(
                    SELECT 1
                    FROM users
                    WHERE no_telepon = @NoTelepon
                      AND (@ExcludedId IS NULL OR id <> @ExcludedId)
                ) AS PhoneExists;
            """;
        var command = new CommandDefinition(
            sql,
            new { Username = username, NoTelepon = phone, ExcludedId = excludedId },
            transaction,
            cancellationToken: cancellationToken);
        var duplicate = await connection.QuerySingleAsync<DuplicateUserCheck>(command);

        if (duplicate.UsernameExists)
        {
            throw new AppException(StatusCodes.Status409Conflict, "Username sudah digunakan.");
        }

        if (duplicate.PhoneExists)
        {
            throw new AppException(StatusCodes.Status409Conflict, "Nomor telepon sudah digunakan.");
        }
    }

    internal static void ValidateIdentityFields(string username, string name)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Username wajib diisi.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Nama wajib diisi.");
        }
    }

    internal static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static async Task<UserModel> GetByIdAsync(
        System.Data.IDbConnection connection,
        uint id,
        System.Data.IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
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
            LIMIT 1;
            """;
        var command = new CommandDefinition(sql, new { Id = id }, transaction, cancellationToken: cancellationToken);
        return await connection.QuerySingleAsync<UserModel>(command);
    }

    private sealed class DuplicateUserCheck
    {
        public bool UsernameExists { get; init; }
        public bool PhoneExists { get; init; }
    }
}
