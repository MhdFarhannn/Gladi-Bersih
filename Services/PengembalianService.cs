using System.Data;
using Dapper;
using Gladi_Bersih.Common;
using Gladi_Bersih.DTOs;
using Gladi_Bersih.Infrastructure;
using Gladi_Bersih.Models;

namespace Gladi_Bersih.Services;

public sealed class PengembalianService : IPengembalianService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IActivityLogService _activityLog;

    public PengembalianService(IDbConnectionFactory connectionFactory, IActivityLogService activityLog)
    {
        _connectionFactory = connectionFactory;
        _activityLog = activityLog;
    }

    public async Task<PagedResult<PengembalianResponse>> GetAllAsync(
        uint actorId,
        string actorRole,
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken)
    {
        var pagination = Pagination.Create(page, pageSize);
        var restrictToOwner = actorRole == "Peminjam";
        var searchPattern = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        const string sql = """
            SELECT COUNT(*)
            FROM pengembalian pg
            INNER JOIN peminjaman p ON p.id = pg.id_peminjaman
            INNER JOIN users peminjam ON peminjam.id = p.id_peminjam
            WHERE pg.deleted_at IS NULL
              AND (@RestrictToOwner = FALSE OR p.id_peminjam = @ActorId)
              AND (
                  @Search IS NULL
                  OR peminjam.username LIKE @Search
                  OR peminjam.nama LIKE @Search
                  OR EXISTS(
                      SELECT 1
                      FROM detail_pengembalian dpg_search
                      INNER JOIN unit_alat ua_search ON ua_search.id = dpg_search.id_unit_alat
                      INNER JOIN alat a_search ON a_search.id = ua_search.id_alat
                      WHERE dpg_search.id_pengembalian = pg.id
                        AND (
                            ua_search.kode_inventaris LIKE @Search
                            OR a_search.nama LIKE @Search
                        )
                  )
              );

            SELECT
                pg.id,
                pg.id_peminjaman AS IdPeminjaman,
                p.id_peminjam AS IdPeminjam,
                peminjam.username AS UsernamePeminjam,
                peminjam.nama AS NamaPeminjam,
                pg.id_petugas AS IdPetugas,
                petugas.nama AS NamaPetugas,
                sp.nama AS StatusPeminjaman,
                p.tanggal_peminjaman AS TanggalPeminjaman,
                p.tanggal_rencana_kembali AS TanggalRencanaKembali,
                pg.tanggal_kembali AS TanggalKembali,
                pg.keterlambatan_pengembalian AS KeterlambatanPengembalian,
                pg.denda,
                pg.catatan
            FROM pengembalian pg
            INNER JOIN peminjaman p ON p.id = pg.id_peminjaman
            INNER JOIN users peminjam ON peminjam.id = p.id_peminjam
            LEFT JOIN users petugas ON petugas.id = pg.id_petugas
            INNER JOIN status_peminjaman sp ON sp.id = p.id_status_peminjaman
            WHERE pg.deleted_at IS NULL
              AND (@RestrictToOwner = FALSE OR p.id_peminjam = @ActorId)
              AND (
                  @Search IS NULL
                  OR peminjam.username LIKE @Search
                  OR peminjam.nama LIKE @Search
                  OR EXISTS(
                      SELECT 1
                      FROM detail_pengembalian dpg_search
                      INNER JOIN unit_alat ua_search ON ua_search.id = dpg_search.id_unit_alat
                      INNER JOIN alat a_search ON a_search.id = ua_search.id_alat
                      WHERE dpg_search.id_pengembalian = pg.id
                        AND (
                            ua_search.kode_inventaris LIKE @Search
                            OR a_search.nama LIKE @Search
                        )
                  )
              )
            ORDER BY pg.id DESC
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
                pagination.PageSize,
                pagination.Offset
            },
            cancellationToken: cancellationToken);
        using var result = await connection.QueryMultipleAsync(command);
        var total = await result.ReadSingleAsync<long>();
        var items = (await result.ReadAsync<PengembalianModel>()).Select(ToResponse).ToArray();
        return new PagedResult<PengembalianResponse>(items, pagination.Page, pagination.PageSize, total);
    }

    public async Task<PengembalianDetailResponse> GetByIdAsync(
        uint actorId,
        string actorRole,
        uint id,
        CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await GetDetailAsync(connection, id, actorId, actorRole, null, cancellationToken);
    }

    public async Task<PengembalianDetailResponse> CreateAsync(
        uint actorId,
        string actorRole,
        PengembalianRequest request,
        CancellationToken cancellationToken)
    {
        var requestedConditions = request.Items ?? [];
        ValidateConditionItems(requestedConditions, false);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        uint returnId;
        try
        {
            var rental = await GetRentalForUpdateAsync(
                connection,
                request.IdPeminjaman,
                transaction,
                cancellationToken);
            EnsureRentalOwnership(rental, actorId, actorRole);
            if (rental.Status != "dipinjam")
            {
                throw new AppException(
                    StatusCodes.Status409Conflict,
                    "Pengembalian hanya dapat dibuat untuk peminjaman berstatus dipinjam.");
            }

            if (request.TanggalKembali < rental.TanggalPeminjaman)
            {
                throw new AppException(
                    StatusCodes.Status400BadRequest,
                    "Tanggal kembali tidak boleh sebelum tanggal peminjaman.");
            }

            await EnsureReturnDoesNotExistAsync(
                connection,
                request.IdPeminjaman,
                transaction,
                cancellationToken);
            var units = await GetAllocatedUnitsAsync(
                connection,
                request.IdPeminjaman,
                transaction,
                cancellationToken);
            if (units.Count == 0)
            {
                throw new AppException(
                    StatusCodes.Status409Conflict,
                    "Tidak ada unit yang dialokasikan pada peminjaman ini.");
            }

            var conditionInputs = BuildConditionInputs(units, requestedConditions);
            await EnsureConditionsExistAsync(
                connection,
                conditionInputs.Select(item => item.StatusKondisi),
                transaction,
                cancellationToken);

            var lateDays = Math.Max(
                0,
                request.TanggalKembali.DayNumber - rental.TanggalRencanaKembali.DayNumber);
            var dailyRate = units.Sum(unit => unit.HargaSewaSaatTransaksi);
            var lateFine = lateDays * dailyRate;
            var officerId = actorRole == "Peminjam" ? null : (uint?)actorId;
            const string headerSql = """
                INSERT INTO pengembalian (
                    id_peminjaman,
                    id_petugas,
                    tanggal_kembali,
                    keterlambatan_pengembalian,
                    denda,
                    catatan
                )
                VALUES (
                    @IdPeminjaman,
                    @IdPetugas,
                    @TanggalKembali,
                    @Keterlambatan,
                    @DendaTelat,
                    @Catatan
                );

                SELECT LAST_INSERT_ID();
                """;
            var headerCommand = new CommandDefinition(
                headerSql,
                new
                {
                    request.IdPeminjaman,
                    IdPetugas = officerId,
                    request.TanggalKembali,
                    Keterlambatan = lateDays,
                    DendaTelat = lateFine,
                    Catatan = AuthService.NormalizeOptional(request.Catatan)
                },
                transaction,
                cancellationToken: cancellationToken);
            returnId = checked((uint)await connection.QuerySingleAsync<ulong>(headerCommand));
            await InsertReturnDetailsAsync(
                connection,
                returnId,
                conditionInputs,
                transaction,
                cancellationToken);

            const string finishSql = """
                UPDATE pengembalian
                SET denda = @DendaTelat + COALESCE((
                    SELECT SUM(denda_kondisi)
                    FROM detail_pengembalian
                    WHERE id_pengembalian = @IdPengembalian
                ), 0)
                WHERE id = @IdPengembalian;

                UPDATE peminjaman
                SET id_status_peminjaman = (
                    SELECT id FROM status_peminjaman WHERE nama = 'selesai' LIMIT 1
                )
                WHERE id = @IdPeminjaman
                  AND deleted_at IS NULL;
                """;
            var finishCommand = new CommandDefinition(
                finishSql,
                new
                {
                    IdPengembalian = returnId,
                    request.IdPeminjaman,
                    DendaTelat = lateFine
                },
                transaction,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(finishCommand);
            await _activityLog.WriteAsync(
                actorId,
                $"Mencatat pengembalian ID {returnId} untuk peminjaman ID {request.IdPeminjaman}.",
                transaction,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        return await GetByIdAsync(actorId, actorRole, returnId, cancellationToken);
    }

    public async Task<PengembalianDetailResponse> VerifyAsync(
        uint actorId,
        uint id,
        VerifikasiPengembalianRequest request,
        CancellationToken cancellationToken)
    {
        var requestedConditions = request.Items ?? [];
        ValidateConditionItems(requestedConditions, true);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var current = await GetReturnForUpdateAsync(connection, id, transaction, cancellationToken);
            var details = await GetDetailsForVerificationAsync(
                connection,
                id,
                transaction,
                cancellationToken);
            var detailsByUnit = details.ToDictionary(detail => detail.IdUnitAlat);

            foreach (var item in requestedConditions)
            {
                if (!detailsByUnit.ContainsKey(item.IdUnitAlat))
                {
                    throw new AppException(
                        StatusCodes.Status400BadRequest,
                        $"Unit ID {item.IdUnitAlat} bukan bagian dari pengembalian ini.");
                }
            }

            var normalizedInputs = requestedConditions.Select(item => new ConditionInput(
                item.IdUnitAlat,
                NormalizeCondition(item.StatusKondisi),
                AuthService.NormalizeOptional(item.Catatan),
                detailsByUnit[item.IdUnitAlat].HargaSewaSaatTransaksi)).ToArray();
            await EnsureConditionsExistAsync(
                connection,
                normalizedInputs.Select(item => item.StatusKondisi),
                transaction,
                cancellationToken);
            await UpdateReturnDetailsAsync(
                connection,
                id,
                normalizedInputs,
                transaction,
                cancellationToken);

            var actualReturnDate = request.TanggalKembali ?? current.TanggalKembali;
            if (actualReturnDate < current.TanggalPeminjaman)
            {
                throw new AppException(
                    StatusCodes.Status400BadRequest,
                    "Tanggal kembali tidak boleh sebelum tanggal peminjaman.");
            }

            var lateDays = Math.Max(
                0,
                actualReturnDate.DayNumber - current.TanggalRencanaKembali.DayNumber);
            var lateFine = lateDays * details.Sum(detail => detail.HargaSewaSaatTransaksi);
            const string updateHeaderSql = """
                UPDATE pengembalian
                SET id_petugas = @IdPetugas,
                    tanggal_kembali = @TanggalKembali,
                    keterlambatan_pengembalian = @Keterlambatan,
                    denda = @DendaTelat + COALESCE((
                        SELECT SUM(denda_kondisi)
                        FROM detail_pengembalian
                        WHERE id_pengembalian = @Id
                    ), 0),
                    catatan = COALESCE(@Catatan, catatan)
                WHERE id = @Id
                  AND deleted_at IS NULL;
                """;
            var updateHeaderCommand = new CommandDefinition(
                updateHeaderSql,
                new
                {
                    Id = id,
                    IdPetugas = actorId,
                    TanggalKembali = actualReturnDate,
                    Keterlambatan = lateDays,
                    DendaTelat = lateFine,
                    Catatan = AuthService.NormalizeOptional(request.Catatan)
                },
                transaction,
                cancellationToken: cancellationToken);
            await connection.ExecuteAsync(updateHeaderCommand);
            await _activityLog.WriteAsync(
                actorId,
                $"Memverifikasi dan menghitung ulang pengembalian ID {id}.",
                transaction,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        return await GetByIdAsync(actorId, "Petugas", id, cancellationToken);
    }

    public async Task DeleteAsync(uint actorId, uint id, CancellationToken cancellationToken)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await GetReturnForUpdateAsync(connection, id, transaction, cancellationToken);
            const string sql = """
                UPDATE pengembalian
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
                $"Menghapus pengembalian ID {id} secara soft delete tanpa mengubah status unit atau peminjaman.",
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

    private static void ValidateConditionItems(
        IReadOnlyCollection<KondisiUnitRequest> items,
        bool required)
    {
        if (required && items.Count == 0)
        {
            throw new AppException(
                StatusCodes.Status400BadRequest,
                "Minimal satu kondisi unit harus diisi.");
        }

        if (items.Count > 100)
        {
            throw new AppException(
                StatusCodes.Status400BadRequest,
                "Maksimal 100 unit per pengembalian.");
        }

        var duplicate = items
            .GroupBy(item => item.IdUnitAlat)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new AppException(
                StatusCodes.Status400BadRequest,
                $"Unit ID {duplicate.Key} tidak boleh muncul lebih dari sekali.");
        }
    }

    private static void EnsureRentalOwnership(
        PeminjamanUntukPengembalianModel rental,
        uint actorId,
        string actorRole)
    {
        if (actorRole == "Peminjam" && rental.IdPeminjam != actorId)
        {
            throw new AppException(
                StatusCodes.Status403Forbidden,
                "Peminjam hanya dapat mengembalikan peminjamannya sendiri.");
        }
    }

    private static void EnsureReturnOwnership(PengembalianModel item, uint actorId, string actorRole)
    {
        if (actorRole == "Peminjam" && item.IdPeminjam != actorId)
        {
            throw new AppException(
                StatusCodes.Status403Forbidden,
                "Anda tidak dapat mengakses pengembalian milik pengguna lain.");
        }
    }

    private static async Task<PeminjamanUntukPengembalianModel> GetRentalForUpdateAsync(
        IDbConnection connection,
        uint id,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                p.id,
                p.id_peminjam AS IdPeminjam,
                sp.nama AS Status,
                p.tanggal_peminjaman AS TanggalPeminjaman,
                p.tanggal_rencana_kembali AS TanggalRencanaKembali
            FROM peminjaman p
            INNER JOIN status_peminjaman sp ON sp.id = p.id_status_peminjaman
            WHERE p.id = @Id
              AND p.deleted_at IS NULL
            LIMIT 1
            FOR UPDATE;
            """;
        var command = new CommandDefinition(sql, new { Id = id }, transaction, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<PeminjamanUntukPengembalianModel>(command)
            ?? throw new AppException(StatusCodes.Status404NotFound, "Peminjaman tidak ditemukan.");
    }

    private static async Task EnsureReturnDoesNotExistAsync(
        IDbConnection connection,
        uint rentalId,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS(
                SELECT 1
                FROM pengembalian
                WHERE id_peminjaman = @IdPeminjaman
            );
            """;
        var command = new CommandDefinition(
            sql,
            new { IdPeminjaman = rentalId },
            transaction,
            cancellationToken: cancellationToken);
        if (await connection.ExecuteScalarAsync<bool>(command))
        {
            throw new AppException(
                StatusCodes.Status409Conflict,
                "Pengembalian untuk peminjaman ini sudah pernah dicatat, termasuk data yang telah dihapus.");
        }
    }

    private static async Task<IReadOnlyCollection<UnitUntukPengembalianModel>> GetAllocatedUnitsAsync(
        IDbConnection connection,
        uint rentalId,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                dup.id_unit_alat AS IdUnitAlat,
                ua.kode_inventaris AS KodeInventaris,
                a.nama AS NamaAlat,
                dp.harga_sewa_saat_transaksi AS HargaSewaSaatTransaksi
            FROM detail_unit_peminjaman dup
            INNER JOIN detail_peminjaman dp ON dp.id = dup.id_detail_peminjaman
            INNER JOIN unit_alat ua ON ua.id = dup.id_unit_alat
            INNER JOIN alat a ON a.id = dp.id_alat
            WHERE dp.id_peminjaman = @IdPeminjaman
              AND ua.deleted_at IS NULL
            ORDER BY dup.id
            FOR UPDATE;
            """;
        var command = new CommandDefinition(
            sql,
            new { IdPeminjaman = rentalId },
            transaction,
            cancellationToken: cancellationToken);
        return (await connection.QueryAsync<UnitUntukPengembalianModel>(command)).ToArray();
    }

    private static IReadOnlyCollection<ConditionInput> BuildConditionInputs(
        IReadOnlyCollection<UnitUntukPengembalianModel> units,
        IReadOnlyCollection<KondisiUnitRequest> requestedConditions)
    {
        var unitsById = units.ToDictionary(unit => unit.IdUnitAlat);
        foreach (var requested in requestedConditions)
        {
            if (!unitsById.ContainsKey(requested.IdUnitAlat))
            {
                throw new AppException(
                    StatusCodes.Status400BadRequest,
                    $"Unit ID {requested.IdUnitAlat} bukan bagian dari peminjaman ini.");
            }
        }

        var requestedById = requestedConditions.ToDictionary(item => item.IdUnitAlat);
        return units.Select(unit =>
        {
            if (!requestedById.TryGetValue(unit.IdUnitAlat, out var requested))
            {
                return new ConditionInput(
                    unit.IdUnitAlat,
                    "baik",
                    null,
                    unit.HargaSewaSaatTransaksi);
            }

            return new ConditionInput(
                unit.IdUnitAlat,
                NormalizeCondition(requested.StatusKondisi),
                AuthService.NormalizeOptional(requested.Catatan),
                unit.HargaSewaSaatTransaksi);
        }).ToArray();
    }

    private static string NormalizeCondition(string value)
    {
        var condition = value.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(condition))
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Status kondisi wajib diisi.");
        }

        return condition;
    }

    private static async Task EnsureConditionsExistAsync(
        IDbConnection connection,
        IEnumerable<string> conditions,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var names = conditions.Distinct(StringComparer.Ordinal).ToArray();
        const string sql = """
            SELECT nama
            FROM status_kondisi_kembali
            WHERE nama IN @Names;
            """;
        var command = new CommandDefinition(sql, new { Names = names }, transaction, cancellationToken: cancellationToken);
        var existing = (await connection.QueryAsync<string>(command)).ToHashSet(StringComparer.Ordinal);
        var missing = names.FirstOrDefault(name => !existing.Contains(name));
        if (missing is not null)
        {
            throw new AppException(
                StatusCodes.Status400BadRequest,
                $"Status kondisi '{missing}' tidak valid.");
        }
    }

    private static async Task InsertReturnDetailsAsync(
        IDbConnection connection,
        uint returnId,
        IReadOnlyCollection<ConditionInput> items,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        parameters.Add("IdPengembalian", returnId);
        var values = new List<string>(items.Count);
        var index = 0;

        foreach (var item in items)
        {
            values.Add($"""
                (
                    @IdPengembalian,
                    @IdUnit{index},
                    (SELECT id FROM status_kondisi_kembali WHERE nama = @Kondisi{index} LIMIT 1),
                    fn_hitung_denda_kondisi(@Kondisi{index}, @Harga{index}),
                    @Catatan{index}
                )
                """);
            parameters.Add($"IdUnit{index}", item.IdUnitAlat);
            parameters.Add($"Kondisi{index}", item.StatusKondisi);
            parameters.Add($"Harga{index}", item.HargaSewaSaatTransaksi);
            parameters.Add($"Catatan{index}", item.Catatan);
            index++;
        }

        var sql = $"""
            INSERT INTO detail_pengembalian (
                id_pengembalian,
                id_unit_alat,
                id_status_kondisi_kembali,
                denda_kondisi,
                catatan
            )
            VALUES {string.Join(", ", values)};
            """;
        var command = new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    private static async Task<PengembalianModel> GetReturnForUpdateAsync(
        IDbConnection connection,
        uint id,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                pg.id,
                pg.id_peminjaman AS IdPeminjaman,
                p.id_peminjam AS IdPeminjam,
                peminjam.username AS UsernamePeminjam,
                peminjam.nama AS NamaPeminjam,
                pg.id_petugas AS IdPetugas,
                petugas.nama AS NamaPetugas,
                sp.nama AS StatusPeminjaman,
                p.tanggal_peminjaman AS TanggalPeminjaman,
                p.tanggal_rencana_kembali AS TanggalRencanaKembali,
                pg.tanggal_kembali AS TanggalKembali,
                pg.keterlambatan_pengembalian AS KeterlambatanPengembalian,
                pg.denda,
                pg.catatan
            FROM pengembalian pg
            INNER JOIN peminjaman p ON p.id = pg.id_peminjaman
            INNER JOIN users peminjam ON peminjam.id = p.id_peminjam
            LEFT JOIN users petugas ON petugas.id = pg.id_petugas
            INNER JOIN status_peminjaman sp ON sp.id = p.id_status_peminjaman
            WHERE pg.id = @Id
              AND pg.deleted_at IS NULL
            LIMIT 1
            FOR UPDATE;
            """;
        var command = new CommandDefinition(sql, new { Id = id }, transaction, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<PengembalianModel>(command)
            ?? throw new AppException(StatusCodes.Status404NotFound, "Pengembalian tidak ditemukan.");
    }

    private static async Task<IReadOnlyCollection<DetailPengembalianModel>> GetDetailsForVerificationAsync(
        IDbConnection connection,
        uint returnId,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                dpg.id,
                dpg.id_unit_alat AS IdUnitAlat,
                ua.kode_inventaris AS KodeInventaris,
                a.nama AS NamaAlat,
                skk.nama AS StatusKondisi,
                dpg.denda_kondisi AS DendaKondisi,
                dpg.catatan,
                dp.harga_sewa_saat_transaksi AS HargaSewaSaatTransaksi
            FROM detail_pengembalian dpg
            INNER JOIN pengembalian pg ON pg.id = dpg.id_pengembalian
            INNER JOIN detail_unit_peminjaman dup ON dup.id_unit_alat = dpg.id_unit_alat
            INNER JOIN detail_peminjaman dp
                ON dp.id = dup.id_detail_peminjaman
                AND dp.id_peminjaman = pg.id_peminjaman
            INNER JOIN unit_alat ua ON ua.id = dpg.id_unit_alat
            INNER JOIN alat a ON a.id = dp.id_alat
            INNER JOIN status_kondisi_kembali skk ON skk.id = dpg.id_status_kondisi_kembali
            WHERE dpg.id_pengembalian = @Id
            ORDER BY dpg.id
            FOR UPDATE;
            """;
        var command = new CommandDefinition(
            sql,
            new { Id = returnId },
            transaction,
            cancellationToken: cancellationToken);
        return (await connection.QueryAsync<DetailPengembalianModel>(command)).ToArray();
    }

    private static async Task UpdateReturnDetailsAsync(
        IDbConnection connection,
        uint returnId,
        IReadOnlyCollection<ConditionInput> items,
        IDbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        parameters.Add("IdPengembalian", returnId);
        var conditionCases = new List<string>(items.Count);
        var fineCases = new List<string>(items.Count);
        var noteCases = new List<string>(items.Count);
        var unitIds = new List<uint>(items.Count);
        var index = 0;

        foreach (var item in items)
        {
            conditionCases.Add($"""
                WHEN @IdUnit{index} THEN (
                    SELECT id FROM status_kondisi_kembali WHERE nama = @Kondisi{index} LIMIT 1
                )
                """);
            fineCases.Add($"""
                WHEN @IdUnit{index} THEN fn_hitung_denda_kondisi(@Kondisi{index}, @Harga{index})
                """);
            noteCases.Add($"WHEN @IdUnit{index} THEN @Catatan{index}");
            parameters.Add($"IdUnit{index}", item.IdUnitAlat);
            parameters.Add($"Kondisi{index}", item.StatusKondisi);
            parameters.Add($"Harga{index}", item.HargaSewaSaatTransaksi);
            parameters.Add($"Catatan{index}", item.Catatan);
            unitIds.Add(item.IdUnitAlat);
            index++;
        }

        parameters.Add("UnitIds", unitIds);
        var sql = $"""
            UPDATE detail_pengembalian
            SET id_status_kondisi_kembali = CASE id_unit_alat
                    {string.Join(" ", conditionCases)}
                    ELSE id_status_kondisi_kembali
                END,
                denda_kondisi = CASE id_unit_alat
                    {string.Join(" ", fineCases)}
                    ELSE denda_kondisi
                END,
                catatan = CASE id_unit_alat
                    {string.Join(" ", noteCases)}
                    ELSE catatan
                END
            WHERE id_pengembalian = @IdPengembalian
              AND id_unit_alat IN @UnitIds;
            """;
        var command = new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    private static async Task<PengembalianDetailResponse> GetDetailAsync(
        IDbConnection connection,
        uint id,
        uint actorId,
        string actorRole,
        IDbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                pg.id,
                pg.id_peminjaman AS IdPeminjaman,
                p.id_peminjam AS IdPeminjam,
                peminjam.username AS UsernamePeminjam,
                peminjam.nama AS NamaPeminjam,
                pg.id_petugas AS IdPetugas,
                petugas.nama AS NamaPetugas,
                sp.nama AS StatusPeminjaman,
                p.tanggal_peminjaman AS TanggalPeminjaman,
                p.tanggal_rencana_kembali AS TanggalRencanaKembali,
                pg.tanggal_kembali AS TanggalKembali,
                pg.keterlambatan_pengembalian AS KeterlambatanPengembalian,
                pg.denda,
                pg.catatan
            FROM pengembalian pg
            INNER JOIN peminjaman p ON p.id = pg.id_peminjaman
            INNER JOIN users peminjam ON peminjam.id = p.id_peminjam
            LEFT JOIN users petugas ON petugas.id = pg.id_petugas
            INNER JOIN status_peminjaman sp ON sp.id = p.id_status_peminjaman
            WHERE pg.id = @Id
              AND pg.deleted_at IS NULL
            LIMIT 1;

            SELECT
                dpg.id,
                dpg.id_unit_alat AS IdUnitAlat,
                ua.kode_inventaris AS KodeInventaris,
                a.nama AS NamaAlat,
                skk.nama AS StatusKondisi,
                dpg.denda_kondisi AS DendaKondisi,
                dpg.catatan
            FROM detail_pengembalian dpg
            INNER JOIN unit_alat ua ON ua.id = dpg.id_unit_alat
            INNER JOIN alat a ON a.id = ua.id_alat
            INNER JOIN status_kondisi_kembali skk ON skk.id = dpg.id_status_kondisi_kembali
            WHERE dpg.id_pengembalian = @Id
            ORDER BY dpg.id;
            """;
        var command = new CommandDefinition(sql, new { Id = id }, transaction, cancellationToken: cancellationToken);
        using var result = await connection.QueryMultipleAsync(command);
        var item = await result.ReadSingleOrDefaultAsync<PengembalianModel>()
            ?? throw new AppException(StatusCodes.Status404NotFound, "Pengembalian tidak ditemukan.");
        EnsureReturnOwnership(item, actorId, actorRole);
        var details = (await result.ReadAsync<DetailPengembalianModel>())
            .Select(detail => new DetailPengembalianResponse(
                detail.Id,
                detail.IdUnitAlat,
                detail.KodeInventaris,
                detail.NamaAlat,
                detail.StatusKondisi,
                detail.DendaKondisi,
                detail.Catatan))
            .ToArray();
        return new PengembalianDetailResponse(
            item.Id,
            item.IdPeminjaman,
            item.IdPeminjam,
            item.UsernamePeminjam,
            item.NamaPeminjam,
            item.IdPetugas,
            item.NamaPetugas,
            item.StatusPeminjaman,
            item.TanggalPeminjaman,
            item.TanggalRencanaKembali,
            item.TanggalKembali,
            item.KeterlambatanPengembalian,
            item.Denda,
            item.Catatan,
            details);
    }

    private static PengembalianResponse ToResponse(PengembalianModel model)
    {
        return new PengembalianResponse(
            model.Id,
            model.IdPeminjaman,
            model.IdPeminjam,
            model.UsernamePeminjam,
            model.NamaPeminjam,
            model.IdPetugas,
            model.NamaPetugas,
            model.StatusPeminjaman,
            model.TanggalPeminjaman,
            model.TanggalRencanaKembali,
            model.TanggalKembali,
            model.KeterlambatanPengembalian,
            model.Denda,
            model.Catatan);
    }

    private sealed record ConditionInput(
        uint IdUnitAlat,
        string StatusKondisi,
        string? Catatan,
        decimal HargaSewaSaatTransaksi);
}
