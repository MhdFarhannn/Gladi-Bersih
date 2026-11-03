using System.Globalization;
using System.Text;
using Dapper;
using Gladi_Bersih.Common;
using Gladi_Bersih.DTOs;
using Gladi_Bersih.Infrastructure;
using Gladi_Bersih.Models;

namespace Gladi_Bersih.Services;

public sealed class LaporanService : ILaporanService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public LaporanService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<LaporanPeminjamanResponse> GetPeminjamanAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        ValidatePeriod(from, to);
        const string sql = """
            SELECT
                id_peminjaman AS IdPeminjaman,
                tanggal_peminjaman AS TanggalPeminjaman,
                tanggal_rencana_kembali AS TanggalRencanaKembali,
                username_peminjam AS UsernamePeminjam,
                nama_peminjam AS NamaPeminjam,
                nama_petugas AS NamaPetugas,
                status_peminjaman AS StatusPeminjaman,
                nama_kategori AS NamaKategori,
                nama_alat AS NamaAlat,
                jumlah,
                harga_sewa_saat_transaksi AS HargaSewaSaatTransaksi,
                subtotal_per_hari AS SubtotalPerHari,
                total_biaya AS TotalBiaya,
                kode_unit AS KodeUnit,
                tanggal_kembali AS TanggalKembali,
                keterlambatan_pengembalian AS KeterlambatanPengembalian,
                denda
            FROM vw_laporan_peminjaman
            WHERE peminjaman_deleted_at IS NULL
              AND tanggal_peminjaman BETWEEN @From AND @To
            ORDER BY tanggal_peminjaman, id_peminjaman, nama_alat;
            """;

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = new CommandDefinition(sql, new { From = from, To = to }, cancellationToken: cancellationToken);
        var models = (await connection.QueryAsync<LaporanPeminjamanModel>(command)).ToArray();
        var items = models.Select(ToPeminjamanRow).ToArray();
        var summary = new RingkasanPeminjaman(
            models.Select(item => item.IdPeminjaman).Distinct().Count(),
            models.Sum(item => item.Jumlah),
            models.GroupBy(item => item.IdPeminjaman).Sum(group => group.First().TotalBiaya));
        return new LaporanPeminjamanResponse(from, to, DateTime.UtcNow, summary, items);
    }

    public async Task<LaporanPengembalianResponse> GetPengembalianAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        ValidatePeriod(from, to);
        const string sql = """
            SELECT
                pg.id AS IdPengembalian,
                pg.id_peminjaman AS IdPeminjaman,
                peminjam.username AS UsernamePeminjam,
                peminjam.nama AS NamaPeminjam,
                petugas.nama AS NamaPetugas,
                p.tanggal_rencana_kembali AS TanggalRencanaKembali,
                pg.tanggal_kembali AS TanggalKembali,
                pg.keterlambatan_pengembalian AS KeterlambatanPengembalian,
                pg.denda AS DendaTotal,
                a.nama AS NamaAlat,
                ua.kode_inventaris AS KodeInventaris,
                skk.nama AS StatusKondisi,
                dpg.denda_kondisi AS DendaKondisi,
                dpg.catatan AS CatatanUnit
            FROM pengembalian pg
            INNER JOIN peminjaman p ON p.id = pg.id_peminjaman
            INNER JOIN users peminjam ON peminjam.id = p.id_peminjam
            LEFT JOIN users petugas ON petugas.id = pg.id_petugas
            INNER JOIN detail_pengembalian dpg ON dpg.id_pengembalian = pg.id
            INNER JOIN unit_alat ua ON ua.id = dpg.id_unit_alat
            INNER JOIN alat a ON a.id = ua.id_alat
            INNER JOIN status_kondisi_kembali skk ON skk.id = dpg.id_status_kondisi_kembali
            WHERE pg.deleted_at IS NULL
              AND pg.tanggal_kembali BETWEEN @From AND @To
            ORDER BY pg.tanggal_kembali, pg.id, ua.kode_inventaris;
            """;

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var command = new CommandDefinition(sql, new { From = from, To = to }, cancellationToken: cancellationToken);
        var models = (await connection.QueryAsync<LaporanPengembalianModel>(command)).ToArray();
        var items = models.Select(ToPengembalianRow).ToArray();
        var summary = new RingkasanPengembalian(
            models.Select(item => item.IdPengembalian).Distinct().Count(),
            models.Length,
            models.GroupBy(item => item.IdPengembalian).Sum(group => group.First().DendaTotal));
        return new LaporanPengembalianResponse(from, to, DateTime.UtcNow, summary, items);
    }

    public byte[] CreatePeminjamanCsv(LaporanPeminjamanResponse report)
    {
        var lines = new List<string>
        {
            "ID Peminjaman,Tanggal Pinjam,Rencana Kembali,Username,Nama Peminjam,Petugas,Status,Kategori,Alat,Jumlah,Harga per Hari,Subtotal per Hari,Total Biaya,Kode Unit,Tanggal Kembali,Keterlambatan,Denda"
        };
        lines.AddRange(report.Items.Select(item => string.Join(",", new[]
        {
            item.IdPeminjaman.ToString(CultureInfo.InvariantCulture),
            item.TanggalPeminjaman.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            item.TanggalRencanaKembali.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            EscapeCsv(item.UsernamePeminjam),
            EscapeCsv(item.NamaPeminjam),
            EscapeCsv(item.NamaPetugas),
            EscapeCsv(item.StatusPeminjaman),
            EscapeCsv(item.NamaKategori),
            EscapeCsv(item.NamaAlat),
            item.Jumlah.ToString(CultureInfo.InvariantCulture),
            item.HargaSewaSaatTransaksi.ToString(CultureInfo.InvariantCulture),
            item.SubtotalPerHari.ToString(CultureInfo.InvariantCulture),
            item.TotalBiaya.ToString(CultureInfo.InvariantCulture),
            EscapeCsv(item.KodeUnit),
            item.TanggalKembali?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            item.KeterlambatanPengembalian?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            item.Denda?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
        })));
        return new UTF8Encoding(true).GetBytes(string.Join(Environment.NewLine, lines));
    }

    public byte[] CreatePengembalianCsv(LaporanPengembalianResponse report)
    {
        var lines = new List<string>
        {
            "ID Pengembalian,ID Peminjaman,Username,Nama Peminjam,Petugas,Rencana Kembali,Tanggal Kembali,Keterlambatan,Denda Total,Alat,Kode Inventaris,Kondisi,Denda Kondisi,Catatan Unit"
        };
        lines.AddRange(report.Items.Select(item => string.Join(",", new[]
        {
            item.IdPengembalian.ToString(CultureInfo.InvariantCulture),
            item.IdPeminjaman.ToString(CultureInfo.InvariantCulture),
            EscapeCsv(item.UsernamePeminjam),
            EscapeCsv(item.NamaPeminjam),
            EscapeCsv(item.NamaPetugas),
            item.TanggalRencanaKembali.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            item.TanggalKembali.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            item.KeterlambatanPengembalian.ToString(CultureInfo.InvariantCulture),
            item.DendaTotal.ToString(CultureInfo.InvariantCulture),
            EscapeCsv(item.NamaAlat),
            EscapeCsv(item.KodeInventaris),
            EscapeCsv(item.StatusKondisi),
            item.DendaKondisi.ToString(CultureInfo.InvariantCulture),
            EscapeCsv(item.CatatanUnit)
        })));
        return new UTF8Encoding(true).GetBytes(string.Join(Environment.NewLine, lines));
    }

    private static void ValidatePeriod(DateOnly from, DateOnly to)
    {
        if (from > to)
        {
            throw new AppException(StatusCodes.Status400BadRequest, "Tanggal awal tidak boleh melewati tanggal akhir.");
        }
    }

    private static LaporanPeminjamanRow ToPeminjamanRow(LaporanPeminjamanModel model)
    {
        return new LaporanPeminjamanRow(
            model.IdPeminjaman,
            model.TanggalPeminjaman,
            model.TanggalRencanaKembali,
            model.UsernamePeminjam,
            model.NamaPeminjam,
            model.NamaPetugas,
            model.StatusPeminjaman,
            model.NamaKategori,
            model.NamaAlat,
            model.Jumlah,
            model.HargaSewaSaatTransaksi,
            model.SubtotalPerHari,
            model.TotalBiaya,
            model.KodeUnit,
            model.TanggalKembali,
            model.KeterlambatanPengembalian,
            model.Denda);
    }

    private static LaporanPengembalianRow ToPengembalianRow(LaporanPengembalianModel model)
    {
        return new LaporanPengembalianRow(
            model.IdPengembalian,
            model.IdPeminjaman,
            model.UsernamePeminjam,
            model.NamaPeminjam,
            model.NamaPetugas,
            model.TanggalRencanaKembali,
            model.TanggalKembali,
            model.KeterlambatanPengembalian,
            model.DendaTotal,
            model.NamaAlat,
            model.KodeInventaris,
            model.StatusKondisi,
            model.DendaKondisi,
            model.CatatanUnit);
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
