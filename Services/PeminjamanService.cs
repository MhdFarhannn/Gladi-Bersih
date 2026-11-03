using System.Data;
using Dapper;
using Gladi_Bersih.Common;
using Gladi_Bersih.DTOs;
using Gladi_Bersih.Infrastructure;
using Gladi_Bersih.Models;
using MySqlConnector;

namespace Gladi_Bersih.Services;

public sealed class PeminjamanService : IPeminjamanService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IActivityLogService _activityLog;

    public PeminjamanService(IDbConnectionFactory connectionFactory, IActivityLogService activityLog)
    {
        _connectionFactory = connectionFactory;
        _activityLog = activityLog;
    }

    public async Task<PagedResult<PeminjamanResponse>> GetAllAsync(
        uint actorId,
        string actorRole,
        int page,
        int pageSize,
        string? search,
        string? status,
        CancellationToken cancellationToken)
    {
        var pagination = Pagination.Create(page, pageSize);
        var searchPattern = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        var statusFilter = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant();
        var restrictToOwner = actorRole == "Peminjam";
        const string sql = """
            SELECT COUNT(*)
            FROM peminjaman p
            INNER JOIN users peminjam ON peminjam.id = p.id_peminjam
            INNER JOIN status_peminjaman sp ON sp.id = p.id_status_peminjaman
            WHERE p.deleted_at IS NULL
              AND (@RestrictToOwner = FALSE OR p.id_peminjam = @ActorId)
              AND (@Status IS NULL OR sp.nama = @Status)
              AND (
                  @Search IS NULL
                  OR peminjam.username LIKE @Search
                  OR peminjam.nama LIKE @Search
                  OR EXISTS(
                      SELECT 1
                      FROM detail_peminjaman dp_search
                      INNER JOIN alat a_search ON a_search.id = dp_search.id_alat
                      WHERE dp_search.id_peminjaman = p.id
                        AND a_search.nama LIKE @Search
                  )
              );

            SELECT
                p.id,
                p.id_peminjam AS IdPeminjam,
                peminjam.username AS UsernamePeminjam,
                peminjam.nama AS NamaPeminjam,
                p.id_petugas AS IdPetugas,
                petugas.nama AS NamaPetugas,
                sp.nama AS Status,
                p.tanggal_peminjaman AS TanggalPeminjaman,
                p.tanggal_rencana_kembali AS TanggalRencanaKembali,
                p.total_biaya AS TotalBiaya,
                p.created_at AS CreatedAt
            FROM peminjaman p
            INNER JOIN users peminjam ON peminjam.id = p.id_peminjam
            LEFT JOIN users petugas ON petugas.id = p.id_petugas
            INNER JOIN status_peminjaman sp ON sp.id = p.id_status_peminjaman
            WHERE p.deleted_at IS NULL
              AND (@RestrictToOwner = FALSE OR p.id_peminjam = @ActorId)
              AND (@Status IS NULL OR sp.nama = @Status)
              AND (
                  @Search IS NULL
                  OR peminjam.username LIKE @Search
                  OR peminjam.nama LIKE @Search
                  OR EXISTS(
                      SELECT 1
                      FROM detail_peminjaman dp_search
                      INNER JOIN alat a_search ON a_search.id = dp_search.id_alat
                      WHERE dp_search.id_peminjaman = p.id
                        AND a_search.nama LIKE @Search
                  )
              )
            ORDER BY p.id DESC
            LIMIT @PageSize OFFSET @Offset;
            """;

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = new CommandDefinition(
            sql,
            new
            {
                ActorId = actorId,
                RestrictToOwner = restrictToOwner,
                Search = searchPattern,
                Status = statusFilter,
                pagination.PageSize,
                pagination.Offset
            },
            cancellationToken: cancellationToken);
        using var result = await connection.QueryMultipleAsync(command);
        var total = await result.ReadSingleAsync<long>();
        var items = (await result.ReadAsync<PeminjamanModel>()).Select(ToResponse).ToArray();
        return new PagedResult<PeminjamanResponse>(items, pagination.Page, pagination.PageSize, total);
    }

    public async Task<PeminjamanDetailResponse> GetByIdAsync(
        uint actorId,
        string actorRole,
        uint id,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await GetDetailAsync(connection, id, actorId, actorRole, null, cancellationToken);
    }

    public async Task<PeminjamanDetailResponse> CreateAsync(
        uint actorId,
        string actorRole,
        PeminjamanRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var borrowerId = ResolveBorrowerId(actorId, actorRole, request.IdPeminjam);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        uint id;
        try
        {
            await EnsureActiveBorrowerAsync(connection, borrowerId, transaction, cancellationToken);
            var devices = await GetRequestedDevicesAsync(connection, request.Items, transaction, cancellationToken);
            var total = CalculateTotal(request, devices);

            const string headerSql = """
                INSERT INTO peminjaman (
                    id_peminjam,
                    id_status_peminjaman,
                    tanggal_peminjaman,
                    tanggal_rencana_kembali,
                    total_biaya
                )
                VALUES (
                    @IdPeminjam,
                    (SELECT id FROM status_peminjaman WHERE nama = 'diajukan' LIMIT 1),
                    @TanggalPeminjaman,
                    @TanggalRencanaKembali,
                    @TotalBiaya
                );

                SELECT LAST_INSERT_ID();
                """;
            var headerCommand = new CommandDefinition(
                headerSql,
                new
                {
                    IdPeminjam = borrowerId,
                    request.TanggalPeminjaman,
                    request.TanggalRencanaKembali,
                    TotalBiaya = total
                },
                transaction,
                cancellationToken: cancellationToken);
            id = checked((uint)await connection.QuerySingleAsync<ulong>(headerCommand));
            await InsertDetailsAsync(connection, id, request.Items, devices, transaction, cancellationToken);
            await _activityLog.WriteAsync(
                actorId,
                $"Mengajukan peminjaman ID {id} untuk Peminjam ID {borrowerId}.",
                transaction,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        return await GetByIdAsync(actorId, actorRole, id, cancellationToken);
    }

    public async Task<PeminjamanDetailResponse> UpdateAsync(
        uint actorId,
        string actorRole,
        uint id,
        PeminjamanRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var current = await GetHeaderForUpdateAsync(connection, id, transaction, cancellationToken);
            EnsureOwnership(current, actorId, actorRole);
            if (current.Status != "diajukan")
            {
                throw new AppException(
                    StatusCodes.Status409Conflict,
                    "Peminjaman hanya dapat diperbarui saat berstatus diajukan.");
            }

            var borrowerId = actorRole == "Peminjam"
                ? actorId
                : ResolveBorrowerId(actorId, actorRole, request.IdPeminjam ?? current.IdPeminjam);
            await EnsureActiveBorrowerAsync(connection, borrowerId, transaction, cancellationToken);
            var devices = await GetRequestedDevicesAsync(connection, request.Items, transaction, cancellationToken);
            var total = CalculateTotal(request, devices);

            const string updateSql = """
                UPDATE peminjaman
                SET id_peminjam = @IdPeminjam,
                    tanggal_peminjaman = @TanggalPeminjaman,
                    tanggal_rencana_kembali = @TanggalRencanaKembali,
                    total_biaya = @TotalBiaya
                WHERE id = @Id
                  AND deleted_at IS NULL;

                DELETE FROM detail_peminjaman
                WHERE id_peminjaman = @Id;
                """;
            var updateCommand = new CommandDefinition(
                updateSql,
                new
                {
                    Id = id,
                    IdPeminjam = borrowerId,
                    request.TanggalPeminjaman,
                    request.TanggalRencanaKembali,
                    TotalBiaya = total
                },
                transaction,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(updateCommand);
            await InsertDetailsAsync(connection, id, request.Items, devices, transaction, cancellationToken);
            await _activityLog.WriteAsync(
                actorId,
                $"Memperbarui peminjaman ID {id}.",
                transaction,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        return await GetByIdAsync(actorId, actorRole, id, cancellationToken);
    }

    public async Task<PeminjamanDetailResponse> ApproveAsync(
        uint officerId,
        uint id,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        try
        {
            var command = new CommandDefinition(
                "sp_setujui_peminjaman",
                new { p_id_peminjaman = id, p_id_petugas = officerId },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(command);
        }
        catch (MySqlException exception) when (exception.Number == 1644)
        {
            throw new AppException(StatusCodes.Status409Conflict, exception.Message);
        }

        await _activityLog.WriteAsync(
            officerId,
            $"Menyetujui peminjaman ID {id} dan mengalokasikan unit.",
            cancellationToken: cancellationToken);
        return await GetByIdAsync(officerId, "Petugas", id, cancellationToken);
    }

    public async Task<PeminjamanDetailResponse> RejectAsync(
        uint officerId,
        uint id,
        string? reason,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var current = await GetHeaderForUpdateAsync(connection, id, transaction, cancellationToken);
            if (current.Status != "diajukan")
            {
                throw new AppException(
                    StatusCodes.Status409Conflict,
                    "Peminjaman hanya dapat ditolak dari status diajukan.");
            }

            const string sql = """
                UPDATE peminjaman
                SET id_petugas = @IdPetugas,
                    id_status_peminjaman = (
                        SELECT id FROM status_peminjaman WHERE nama = 'ditolak' LIMIT 1
                    )
                WHERE id = @Id
                  AND deleted_at IS NULL;
                """;
            var command = new CommandDefinition(
                sql,
                new { Id = id, IdPetugas = officerId },
                transaction,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(command);
            var reasonText = string.IsNullOrWhiteSpace(reason) ? string.Empty : $" Alasan: {reason.Trim()}";
            await _activityLog.WriteAsync(
                officerId,
                $"Menolak peminjaman ID {id}.{reasonText}",
                transaction,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        return await GetByIdAsync(officerId, "Petugas", id, cancellationToken);
    }

    public async Task<PeminjamanDetailResponse> HandOverAsync(
        uint officerId,
        uint id,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var current = await GetHeaderForUpdateAsync(connection, id, transaction, cancellationToken);
            if (current.Status != "disetujui")
            {
                throw new AppException(
                    StatusCodes.Status409Conflict,
                    "Penyerahan hanya dapat dilakukan dari status disetujui.");
            }

            const string sql = """
                UPDATE peminjaman
                SET id_status_peminjaman = (
                    SELECT id FROM status_peminjaman WHERE nama = 'dipinjam' LIMIT 1
                )
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
                officerId,
                $"Menyerahkan unit untuk peminjaman ID {id}.",
                transaction,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        return await GetByIdAsync(officerId, "Petugas", id, cancellationToken);
    }

    public async Task DeleteAsync(uint actorId, uint id, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var current = await GetHeaderForUpdateAsync(connection, id, transaction, cancellationToken);
            if (current.Status is not ("selesai" or "ditolak"))
            {
                throw new AppException(
                    StatusCodes.Status409Conflict,
                    "Hanya peminjaman selesai atau ditolak yang dapat dihapus.");
            }

            const string sql = """
                UPDATE peminjaman
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
                $"Menghapus peminjaman ID {id} secara soft delete.",
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

    private static void ValidateRequest(PeminjamanRequest request)
    {
        if (request.TanggalRencanaKembali < request.TanggalPeminjaman)
        {
            throw new AppException(
                StatusCodes.Status400BadRequest,
                "Tanggal rencana kembali tidak boleh sebelum tanggal peminjaman.");
        }

        if (request.Items.Count is < 1 or > 100)
        {
            throw new AppException(
                StatusCodes.Status400BadRequest,
                "Jumlah jenis alat harus antara 1 dan 100.");
        }

        if (request.Items.Any(item => item.Jumlah <= 0))
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Jumlah alat harus lebih dari 0.");
        }

        var duplicate = request.Items
            .GroupBy(item => item.IdAlat)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new AppException(
                StatusCodes.Status400BadRequest,
                $"Alat ID {duplicate.Key} tidak boleh muncul lebih dari sekali.");
        }
    }

    private static uint ResolveBorrowerId(uint actorId, string actorRole, uint? requestedBorrowerId)
    {
        if (actorRole == "Peminjam")
        {
            if (requestedBorrowerId.HasValue && requestedBorrowerId.Value != actorId)
            {
                throw new AppException(
                    StatusCodes.Status403Forbidden,
                    "Peminjam hanya dapat mengajukan peminjaman untuk dirinya sendiri.");
            }

            return actorId;
        }

        if (actorRole == "Admin" && requestedBorrowerId.HasValue && requestedBorrowerId.Value > 0)
        {
            return requestedBorrowerId.Value;
        }

        throw new AppException(StatusCodes.Status400BadRequest, "ID peminjam wajib diisi.");
    }

    private static void EnsureOwnership(PeminjamanModel rental, uint actorId, string actorRole)
    {
        if (actorRole == "Peminjam" && rental.IdPeminjam != actorId)
        {
            throw new AppException(
                StatusCodes.Status403Forbidden,
                "Anda tidak dapat mengakses peminjaman milik pengguna lain.");
        }
    }

    private static async Task EnsureActiveBorrowerAsync(
        IDbConnection connection,
        uint borrowerId,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS(
                SELECT 1
                FROM users u
                INNER JOIN roles r ON r.id = u.id_role
                WHERE u.id = @Id
                  AND u.deleted_at IS NULL
                  AND r.nama = 'Peminjam'
            );
            """;
        var command = new CommandDefinition(
            sql,
            new { Id = borrowerId },
            transaction,
            cancellationToken: cancellationToken);
        if (!await connection.ExecuteScalarAsync<bool>(command))
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Peminjam aktif tidak ditemukan.");
        }
    }

    private static async Task<IReadOnlyDictionary<uint, KetersediaanAlatModel>> GetRequestedDevicesAsync(
        IDbConnection connection,
        IReadOnlyCollection<PeminjamanItemRequest> items,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                a.id,
                a.nama,
                a.harga_sewa AS HargaSewa,
                COALESCE(SUM(
                    CASE
                        WHEN ua.deleted_at IS NULL AND sk.nama = 'tersedia' THEN 1
                        ELSE 0
                    END
                ), 0) AS StokTersedia
            FROM alat a
            INNER JOIN kategori k ON k.id = a.id_kategori
            LEFT JOIN unit_alat ua ON ua.id_alat = a.id
            LEFT JOIN status_ketersediaan sk ON sk.id = ua.id_status_ketersediaan
            WHERE a.id IN @Ids
              AND a.deleted_at IS NULL
              AND k.deleted_at IS NULL
            GROUP BY a.id, a.nama, a.harga_sewa;
            """;
        var ids = items.Select(item => item.IdAlat).ToArray();
        var command = new CommandDefinition(sql, new { Ids = ids }, transaction, cancellationToken: cancellationToken);
        var devices = (await connection.QueryAsync<KetersediaanAlatModel>(command))
            .ToDictionary(device => device.Id);

        foreach (var item in items)
        {
            if (!devices.TryGetValue(item.IdAlat, out var device))
            {
                throw new AppException(
                    StatusCodes.Status400BadRequest,
                    $"Alat ID {item.IdAlat} tidak ditemukan atau tidak aktif.");
            }

            if (device.StokTersedia < item.Jumlah)
            {
                throw new AppException(
                    StatusCodes.Status409Conflict,
                    $"Stok tersedia untuk '{device.Nama}' hanya {device.StokTersedia}, diminta {item.Jumlah}.");
            }
        }

        return devices;
    }

    private static decimal CalculateTotal(
        PeminjamanRequest request,
        IReadOnlyDictionary<uint, KetersediaanAlatModel> devices)
    {
        var days = Math.Max(1, request.TanggalRencanaKembali.DayNumber - request.TanggalPeminjaman.DayNumber);
        return request.Items.Sum(item => item.Jumlah * devices[item.IdAlat].HargaSewa) * days;
    }

    private static async Task InsertDetailsAsync(
        IDbConnection connection,
        uint rentalId,
        IReadOnlyCollection<PeminjamanItemRequest> items,
        IReadOnlyDictionary<uint, KetersediaanAlatModel> devices,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        parameters.Add("IdPeminjaman", rentalId);
        var values = new List<string>(items.Count);
        var index = 0;

        foreach (var item in items)
        {
            values.Add($"(@IdPeminjaman, @IdAlat{index}, @Jumlah{index}, @Harga{index})");
            parameters.Add($"IdAlat{index}", item.IdAlat);
            parameters.Add($"Jumlah{index}", item.Jumlah);
            parameters.Add($"Harga{index}", devices[item.IdAlat].HargaSewa);
            index++;
        }

        var sql = $"""
            INSERT INTO detail_peminjaman (
                id_peminjaman,
                id_alat,
                jumlah,
                harga_sewa_saat_transaksi
            )
            VALUES {string.Join(", ", values)};
            """;
        var command = new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    private static async Task<PeminjamanModel> GetHeaderForUpdateAsync(
        IDbConnection connection,
        uint id,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                p.id,
                p.id_peminjam AS IdPeminjam,
                peminjam.username AS UsernamePeminjam,
                peminjam.nama AS NamaPeminjam,
                p.id_petugas AS IdPetugas,
                petugas.nama AS NamaPetugas,
                sp.nama AS Status,
                p.tanggal_peminjaman AS TanggalPeminjaman,
                p.tanggal_rencana_kembali AS TanggalRencanaKembali,
                p.total_biaya AS TotalBiaya,
                p.created_at AS CreatedAt
            FROM peminjaman p
            INNER JOIN users peminjam ON peminjam.id = p.id_peminjam
            LEFT JOIN users petugas ON petugas.id = p.id_petugas
            INNER JOIN status_peminjaman sp ON sp.id = p.id_status_peminjaman
            WHERE p.id = @Id
              AND p.deleted_at IS NULL
            LIMIT 1
            FOR UPDATE;
            """;
        var command = new CommandDefinition(sql, new { Id = id }, transaction, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<PeminjamanModel>(command)
            ?? throw new AppException(StatusCodes.Status404NotFound, "Peminjaman tidak ditemukan.");
    }

    private static async Task<PeminjamanDetailResponse> GetDetailAsync(
        IDbConnection connection,
        uint id,
        uint actorId,
        string actorRole,
        IDbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                p.id,
                p.id_peminjam AS IdPeminjam,
                peminjam.username AS UsernamePeminjam,
                peminjam.nama AS NamaPeminjam,
                p.id_petugas AS IdPetugas,
                petugas.nama AS NamaPetugas,
                sp.nama AS Status,
                p.tanggal_peminjaman AS TanggalPeminjaman,
                p.tanggal_rencana_kembali AS TanggalRencanaKembali,
                p.total_biaya AS TotalBiaya,
                p.created_at AS CreatedAt
            FROM peminjaman p
            INNER JOIN users peminjam ON peminjam.id = p.id_peminjam
            LEFT JOIN users petugas ON petugas.id = p.id_petugas
            INNER JOIN status_peminjaman sp ON sp.id = p.id_status_peminjaman
            WHERE p.id = @Id
              AND p.deleted_at IS NULL
            LIMIT 1;

            SELECT
                dp.id,
                dp.id_alat AS IdAlat,
                a.nama AS NamaAlat,
                dp.jumlah,
                dp.harga_sewa_saat_transaksi AS HargaSewaSaatTransaksi
            FROM detail_peminjaman dp
            INNER JOIN alat a ON a.id = dp.id_alat
            WHERE dp.id_peminjaman = @Id
            ORDER BY dp.id;

            SELECT
                dup.id_detail_peminjaman AS IdDetailPeminjaman,
                dup.id_unit_alat AS IdUnitAlat,
                ua.kode_inventaris AS KodeInventaris
            FROM detail_unit_peminjaman dup
            INNER JOIN detail_peminjaman dp ON dp.id = dup.id_detail_peminjaman
            INNER JOIN unit_alat ua ON ua.id = dup.id_unit_alat
            WHERE dp.id_peminjaman = @Id
            ORDER BY dup.id;
            """;
        var command = new CommandDefinition(sql, new { Id = id }, transaction, cancellationToken: cancellationToken);
        using var result = await connection.QueryMultipleAsync(command);
        var rental = await result.ReadSingleOrDefaultAsync<PeminjamanModel>()
            ?? throw new AppException(StatusCodes.Status404NotFound, "Peminjaman tidak ditemukan.");
        EnsureOwnership(rental, actorId, actorRole);
        var details = (await result.ReadAsync<DetailPeminjamanModel>()).ToArray();
        var units = (await result.ReadAsync<UnitPeminjamanModel>())
            .GroupBy(unit => unit.IdDetailPeminjaman)
            .ToDictionary(group => group.Key, group => group
                .Select(unit => new UnitPeminjamanResponse(unit.IdUnitAlat, unit.KodeInventaris))
                .ToArray());
        var detailResponses = details.Select(detail => new DetailPeminjamanResponse(
            detail.Id,
            detail.IdAlat,
            detail.NamaAlat,
            detail.Jumlah,
            detail.HargaSewaSaatTransaksi,
            units.GetValueOrDefault(detail.Id, []))).ToArray();

        return new PeminjamanDetailResponse(
            rental.Id,
            rental.IdPeminjam,
            rental.UsernamePeminjam,
            rental.NamaPeminjam,
            rental.IdPetugas,
            rental.NamaPetugas,
            rental.Status,
            rental.TanggalPeminjaman,
            rental.TanggalRencanaKembali,
            rental.TotalBiaya,
            rental.CreatedAt,
            detailResponses);
    }

    private static PeminjamanResponse ToResponse(PeminjamanModel model)
    {
        return new PeminjamanResponse(
            model.Id,
            model.IdPeminjam,
            model.UsernamePeminjam,
            model.NamaPeminjam,
            model.IdPetugas,
            model.NamaPetugas,
            model.Status,
            model.TanggalPeminjaman,
            model.TanggalRencanaKembali,
            model.TotalBiaya,
            model.CreatedAt);
    }
}
