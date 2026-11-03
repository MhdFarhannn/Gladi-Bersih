using Dapper;
using Gladi_Bersih.Common;
using Gladi_Bersih.DTOs;
using Gladi_Bersih.Infrastructure;
using Gladi_Bersih.Models;

namespace Gladi_Bersih.Services;

public sealed class LogAktivitasService : ILogAktivitasService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public LogAktivitasService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PagedResult<LogAktivitasResponse>> GetAllAsync(
        int page,
        int pageSize,
        string? search,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken)
    {
        var pagination = Pagination.Create(page, pageSize);
        if (from.HasValue && to.HasValue && from > to)
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Tanggal awal tidak boleh melewati tanggal akhir.");
        }

        var searchPattern = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        const string sql = """
            SELECT COUNT(*)
            FROM log_aktivitas la
            LEFT JOIN users u ON u.id = la.id_user
            WHERE (@Search IS NULL
                   OR la.aktivitas LIKE @Search
                   OR u.username LIKE @Search
                   OR u.nama LIKE @Search)
              AND (@From IS NULL OR la.waktu >= @From)
              AND (@To IS NULL OR la.waktu < DATE_ADD(@To, INTERVAL 1 DAY));

            SELECT
                la.id,
                la.id_user AS IdUser,
                u.username,
                u.nama AS NamaUser,
                la.aktivitas,
                la.waktu
            FROM log_aktivitas la
            LEFT JOIN users u ON u.id = la.id_user
            WHERE (@Search IS NULL
                   OR la.aktivitas LIKE @Search
                   OR u.username LIKE @Search
                   OR u.nama LIKE @Search)
              AND (@From IS NULL OR la.waktu >= @From)
              AND (@To IS NULL OR la.waktu < DATE_ADD(@To, INTERVAL 1 DAY))
            ORDER BY la.id DESC
            LIMIT @PageSize OFFSET @Offset;
            """;

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = new CommandDefinition(
            sql,
            new
            {
                Search = searchPattern,
                From = from,
                To = to,
                pagination.PageSize,
                pagination.Offset
            },
            cancellationToken: cancellationToken);
        using var result = await connection.QueryMultipleAsync(command);
        var total = await result.ReadSingleAsync<long>();
        var items = (await result.ReadAsync<LogAktivitasModel>())
            .Select(item => new LogAktivitasResponse(
                item.Id,
                item.IdUser,
                item.Username,
                item.NamaUser,
                item.Aktivitas,
                item.Waktu))
            .ToArray();
        return new PagedResult<LogAktivitasResponse>(items, pagination.Page, pagination.PageSize, total);
    }
}
