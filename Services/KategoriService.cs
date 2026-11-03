using System.Data;
using Dapper;
using Gladi_Bersih.Common;
using Gladi_Bersih.DTOs;
using Gladi_Bersih.Infrastructure;
using Gladi_Bersih.Models;

namespace Gladi_Bersih.Services;

public sealed class KategoriService : IKategoriService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IActivityLogService _activityLog;

    public KategoriService(IDbConnectionFactory connectionFactory, IActivityLogService activityLog)
    {
        _connectionFactory = connectionFactory;
        _activityLog = activityLog;
    }

    public async Task<PagedResult<KategoriResponse>> GetAllAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken)
    {
        var pagination = Pagination.Create(page, pageSize);
        var searchPattern = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        const string sql = """
            SELECT COUNT(*)
            FROM kategori
            WHERE deleted_at IS NULL
              AND (@Search IS NULL OR nama LIKE @Search);

            SELECT id, nama
            FROM kategori
            WHERE deleted_at IS NULL
              AND (@Search IS NULL OR nama LIKE @Search)
            ORDER BY nama
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
        var items = (await result.ReadAsync<KategoriModel>()).Select(ToResponse).ToArray();
        return new PagedResult<KategoriResponse>(items, pagination.Page, pagination.PageSize, total);
    }

    public async Task<KategoriResponse> GetByIdAsync(uint id, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return ToResponse(await GetActiveByIdAsync(connection, id, null, cancellationToken));
    }

    public async Task<KategoriResponse> CreateAsync(
        uint actorId,
        KategoriRequest request,
        CancellationToken cancellationToken)
    {
        var name = NormalizeName(request.Nama);
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await EnsureUniqueAsync(connection, name, null, transaction, cancellationToken);
            const string sql = """
                INSERT INTO kategori (nama) VALUES (@Nama);
                SELECT LAST_INSERT_ID();
                """;
            var command = new CommandDefinition(sql, new { Nama = name }, transaction, cancellationToken: cancellationToken);
            var id = checked((uint)await connection.QuerySingleAsync<ulong>(command));
            await _activityLog.WriteAsync(
                actorId,
                $"Membuat kategori '{name}' (ID {id}).",
                transaction,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new KategoriResponse(id, name);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<KategoriResponse> UpdateAsync(
        uint actorId,
        uint id,
        KategoriRequest request,
        CancellationToken cancellationToken)
    {
        var name = NormalizeName(request.Nama);
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await GetActiveByIdAsync(connection, id, transaction, cancellationToken, true);
            await EnsureUniqueAsync(connection, name, id, transaction, cancellationToken);
            const string sql = """
                UPDATE kategori
                SET nama = @Nama
                WHERE id = @Id
                  AND deleted_at IS NULL;
                """;
            var command = new CommandDefinition(
                sql,
                new { Id = id, Nama = name },
                transaction,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(command);
            await _activityLog.WriteAsync(
                actorId,
                $"Memperbarui kategori '{name}' (ID {id}).",
                transaction,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new KategoriResponse(id, name);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task DeleteAsync(uint actorId, uint id, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var category = await GetActiveByIdAsync(connection, id, transaction, cancellationToken, true);
            const string guardSql = """
                SELECT EXISTS(
                    SELECT 1
                    FROM alat
                    WHERE id_kategori = @Id
                      AND deleted_at IS NULL
                );
                """;
            var guardCommand = new CommandDefinition(
                guardSql,
                new { Id = id },
                transaction,
                cancellationToken: cancellationToken);
            if (await connection.ExecuteScalarAsync<bool>(guardCommand))
            {
                throw new AppException(
                    StatusCodes.Status409Conflict,
                    "Kategori tidak dapat dihapus karena masih memiliki alat aktif.");
            }

            const string deleteSql = """
                UPDATE kategori
                SET deleted_at = NOW()
                WHERE id = @Id
                  AND deleted_at IS NULL;
                """;
            var deleteCommand = new CommandDefinition(
                deleteSql,
                new { Id = id },
                transaction,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(deleteCommand);
            await _activityLog.WriteAsync(
                actorId,
                $"Menghapus kategori '{category.Nama}' (ID {id}) secara soft delete.",
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

    private static string NormalizeName(string value)
    {
        var name = value.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Nama kategori wajib diisi.");
        }

        return name;
    }

    private static async Task EnsureUniqueAsync(
        IDbConnection connection,
        string name,
        uint? excludedId,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS(
                SELECT 1
                FROM kategori
                WHERE nama = @Nama
                  AND (@ExcludedId IS NULL OR id <> @ExcludedId)
            );
            """;
        var command = new CommandDefinition(
            sql,
            new { Nama = name, ExcludedId = excludedId },
            transaction,
            cancellationToken: cancellationToken);
        if (await connection.ExecuteScalarAsync<bool>(command))
        {
            throw new AppException(
                StatusCodes.Status409Conflict,
                "Nama kategori sudah digunakan, termasuk oleh data yang telah dihapus.");
        }
    }

    private static async Task<KategoriModel> GetActiveByIdAsync(
        IDbConnection connection,
        uint id,
        IDbTransaction? transaction,
        CancellationToken cancellationToken,
        bool forUpdate = false)
    {
        var sql = """
            SELECT id, nama
            FROM kategori
            WHERE id = @Id
              AND deleted_at IS NULL
            LIMIT 1
            """ + (forUpdate ? " FOR UPDATE;" : ";");
        var command = new CommandDefinition(sql, new { Id = id }, transaction, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<KategoriModel>(command)
            ?? throw new AppException(StatusCodes.Status404NotFound, "Kategori tidak ditemukan.");
    }

    private static KategoriResponse ToResponse(KategoriModel model)
    {
        return new KategoriResponse(model.Id, model.Nama);
    }
}
