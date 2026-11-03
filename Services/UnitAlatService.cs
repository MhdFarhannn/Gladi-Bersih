using System.Data;
using Dapper;
using Gladi_Bersih.Common;
using Gladi_Bersih.DTOs;
using Gladi_Bersih.Infrastructure;
using Gladi_Bersih.Models;

namespace Gladi_Bersih.Services;

public sealed class UnitAlatService : IUnitAlatService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IActivityLogService _activityLog;

    public UnitAlatService(IDbConnectionFactory connectionFactory, IActivityLogService activityLog)
    {
        _connectionFactory = connectionFactory;
        _activityLog = activityLog;
    }

    public async Task<PagedResult<UnitAlatResponse>> GetAllAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken)
    {
        var pagination = Pagination.Create(page, pageSize);
        var searchPattern = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        const string sql = """
            SELECT COUNT(*)
            FROM unit_alat ua
            INNER JOIN alat a ON a.id = ua.id_alat
            WHERE ua.deleted_at IS NULL
              AND a.deleted_at IS NULL
              AND (@Search IS NULL OR ua.kode_inventaris LIKE @Search OR a.nama LIKE @Search);

            SELECT
                ua.id,
                ua.id_alat AS IdAlat,
                a.nama AS Alat,
                ua.kode_inventaris AS KodeInventaris,
                sk.nama AS StatusKetersediaan,
                sb.nama AS StatusBarang,
                ua.created_at AS CreatedAt
            FROM unit_alat ua
            INNER JOIN alat a ON a.id = ua.id_alat
            INNER JOIN status_ketersediaan sk ON sk.id = ua.id_status_ketersediaan
            INNER JOIN status_barang sb ON sb.id = ua.id_status_barang
            WHERE ua.deleted_at IS NULL
              AND a.deleted_at IS NULL
              AND (@Search IS NULL OR ua.kode_inventaris LIKE @Search OR a.nama LIKE @Search)
            ORDER BY ua.id DESC
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
        var items = (await result.ReadAsync<UnitAlatModel>()).Select(ToResponse).ToArray();
        return new PagedResult<UnitAlatResponse>(items, pagination.Page, pagination.PageSize, total);
    }

    public async Task<UnitAlatResponse> GetByIdAsync(uint id, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return ToResponse(await GetActiveByIdAsync(connection, id, null, cancellationToken));
    }

    public async Task<UnitAlatResponse> CreateAsync(
        uint actorId,
        UnitAlatCreateRequest request,
        CancellationToken cancellationToken)
    {
        var code = NormalizeInventoryCode(request.KodeInventaris);
        var availability = NormalizeStatus(request.StatusKetersediaan);
        var condition = NormalizeStatus(request.StatusBarang);
        if (availability == "dipinjam")
        {
            throw new AppException(
                StatusCodes.Status409Conflict,
                "Status dipinjam hanya dapat ditetapkan melalui proses persetujuan peminjaman.");
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await EnsureActiveDeviceAsync(connection, request.IdAlat, transaction, cancellationToken);
            await EnsureStatusesAsync(connection, availability, condition, transaction, cancellationToken);
            await EnsureUniqueCodeAsync(connection, code, null, transaction, cancellationToken);
            const string sql = """
                INSERT INTO unit_alat (
                    id_alat,
                    id_status_ketersediaan,
                    kode_inventaris,
                    id_status_barang
                )
                VALUES (
                    @IdAlat,
                    (SELECT id FROM status_ketersediaan WHERE nama = @StatusKetersediaan LIMIT 1),
                    @KodeInventaris,
                    (SELECT id FROM status_barang WHERE nama = @StatusBarang LIMIT 1)
                );

                SELECT LAST_INSERT_ID();
                """;
            var command = new CommandDefinition(
                sql,
                new
                {
                    request.IdAlat,
                    KodeInventaris = code,
                    StatusKetersediaan = availability,
                    StatusBarang = condition
                },
                transaction,
                cancellationToken: cancellationToken);
            var id = checked((uint)await connection.QuerySingleAsync<ulong>(command));
            var unit = await GetActiveByIdAsync(connection, id, transaction, cancellationToken);
            await _activityLog.WriteAsync(
                actorId,
                $"Membuat unit alat '{code}' (ID {id}).",
                transaction,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToResponse(unit);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<UnitAlatResponse> UpdateAsync(
        uint actorId,
        uint id,
        UnitAlatUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var code = NormalizeInventoryCode(request.KodeInventaris);
        var availability = NormalizeStatus(request.StatusKetersediaan);
        var condition = NormalizeStatus(request.StatusBarang);
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var current = await GetActiveByIdAsync(connection, id, transaction, cancellationToken, true);
            if ((current.StatusKetersediaan == "dipinjam" || availability == "dipinjam")
                && (current.IdAlat != request.IdAlat
                    || current.StatusKetersediaan != availability
                    || current.StatusBarang != condition))
            {
                throw new AppException(
                    StatusCodes.Status409Conflict,
                    "Status atau alat induk unit yang dipinjam tidak dapat diubah secara manual.");
            }

            await EnsureActiveDeviceAsync(connection, request.IdAlat, transaction, cancellationToken);
            await EnsureStatusesAsync(connection, availability, condition, transaction, cancellationToken);
            await EnsureUniqueCodeAsync(connection, code, id, transaction, cancellationToken);
            const string sql = """
                UPDATE unit_alat
                SET id_alat = @IdAlat,
                    id_status_ketersediaan = (
                        SELECT id FROM status_ketersediaan WHERE nama = @StatusKetersediaan LIMIT 1
                    ),
                    kode_inventaris = @KodeInventaris,
                    id_status_barang = (
                        SELECT id FROM status_barang WHERE nama = @StatusBarang LIMIT 1
                    )
                WHERE id = @Id
                  AND deleted_at IS NULL;
                """;
            var command = new CommandDefinition(
                sql,
                new
                {
                    Id = id,
                    request.IdAlat,
                    KodeInventaris = code,
                    StatusKetersediaan = availability,
                    StatusBarang = condition
                },
                transaction,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(command);
            var unit = await GetActiveByIdAsync(connection, id, transaction, cancellationToken);
            await _activityLog.WriteAsync(
                actorId,
                $"Memperbarui unit alat '{code}' (ID {id}).",
                transaction,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToResponse(unit);
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
            var unit = await GetActiveByIdAsync(connection, id, transaction, cancellationToken, true);
            if (unit.StatusKetersediaan == "dipinjam")
            {
                throw new AppException(
                    StatusCodes.Status409Conflict,
                    "Unit alat tidak dapat dihapus karena sedang dipinjam.");
            }

            const string sql = """
                UPDATE unit_alat
                SET deleted_at = NOW()
                WHERE id = @Id
                  AND deleted_at IS NULL;
                """;
            var command = new CommandDefinition(
                sql,
                new { Id = id },
                transaction,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(command);
            await _activityLog.WriteAsync(
                actorId,
                $"Menghapus unit alat '{unit.KodeInventaris}' (ID {id}) secara soft delete.",
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

    private static string NormalizeInventoryCode(string value)
    {
        var code = value.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Kode inventaris wajib diisi.");
        }

        return code;
    }

    private static string NormalizeStatus(string value)
    {
        var status = value.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(status))
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Status wajib diisi.");
        }

        return status;
    }

    private static async Task EnsureActiveDeviceAsync(
        IDbConnection connection,
        uint deviceId,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS(
                SELECT 1
                FROM alat
                WHERE id = @Id
                  AND deleted_at IS NULL
            );
            """;
        var command = new CommandDefinition(
            sql,
            new { Id = deviceId },
            transaction,
            cancellationToken: cancellationToken);
        if (!await connection.ExecuteScalarAsync<bool>(command))
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Alat aktif tidak ditemukan.");
        }
    }

    private static async Task EnsureStatusesAsync(
        IDbConnection connection,
        string availability,
        string condition,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                EXISTS(
                    SELECT 1 FROM status_ketersediaan WHERE nama = @Availability
                ) AS AvailabilityExists,
                EXISTS(
                    SELECT 1 FROM status_barang WHERE nama = @Condition
                ) AS ConditionExists;
            """;
        var command = new CommandDefinition(
            sql,
            new { Availability = availability, Condition = condition },
            transaction,
            cancellationToken: cancellationToken);
        var statuses = await connection.QuerySingleAsync<StatusCheck>(command);
        if (!statuses.AvailabilityExists)
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Status ketersediaan tidak valid.");
        }

        if (!statuses.ConditionExists)
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Status barang tidak valid.");
        }
    }

    private static async Task EnsureUniqueCodeAsync(
        IDbConnection connection,
        string code,
        uint? excludedId,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS(
                SELECT 1
                FROM unit_alat
                WHERE kode_inventaris = @Code
                  AND (@ExcludedId IS NULL OR id <> @ExcludedId)
            );
            """;
        var command = new CommandDefinition(
            sql,
            new { Code = code, ExcludedId = excludedId },
            transaction,
            cancellationToken: cancellationToken);
        if (await connection.ExecuteScalarAsync<bool>(command))
        {
            throw new AppException(
                StatusCodes.Status409Conflict,
                "Kode inventaris sudah digunakan, termasuk oleh data yang telah dihapus.");
        }
    }

    private static async Task<UnitAlatModel> GetActiveByIdAsync(
        IDbConnection connection,
        uint id,
        IDbTransaction? transaction,
        CancellationToken cancellationToken,
        bool forUpdate = false)
    {
        var sql = """
            SELECT
                ua.id,
                ua.id_alat AS IdAlat,
                a.nama AS Alat,
                ua.kode_inventaris AS KodeInventaris,
                sk.nama AS StatusKetersediaan,
                sb.nama AS StatusBarang,
                ua.created_at AS CreatedAt
            FROM unit_alat ua
            INNER JOIN alat a ON a.id = ua.id_alat
            INNER JOIN status_ketersediaan sk ON sk.id = ua.id_status_ketersediaan
            INNER JOIN status_barang sb ON sb.id = ua.id_status_barang
            WHERE ua.id = @Id
              AND ua.deleted_at IS NULL
              AND a.deleted_at IS NULL
            LIMIT 1
            """ + (forUpdate ? " FOR UPDATE;" : ";");
        var command = new CommandDefinition(sql, new { Id = id }, transaction, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<UnitAlatModel>(command)
            ?? throw new AppException(StatusCodes.Status404NotFound, "Unit alat tidak ditemukan.");
    }

    private static UnitAlatResponse ToResponse(UnitAlatModel model)
    {
        return new UnitAlatResponse(
            model.Id,
            model.IdAlat,
            model.Alat,
            model.KodeInventaris,
            model.StatusKetersediaan,
            model.StatusBarang,
            model.CreatedAt);
    }

    private sealed class StatusCheck
    {
        public bool AvailabilityExists { get; init; }
        public bool ConditionExists { get; init; }
    }
}
