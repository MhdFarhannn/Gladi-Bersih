using System.ComponentModel.DataAnnotations;

namespace Gladi_Bersih.DTOs;

public sealed class PengembalianRequest
{
    [Range(1, uint.MaxValue, ErrorMessage = "ID peminjaman tidak valid.")]
    public uint IdPeminjaman { get; init; }

    [Required(ErrorMessage = "Tanggal kembali wajib diisi.")]
    public DateOnly TanggalKembali { get; init; }

    [MaxLength(2000, ErrorMessage = "Catatan maksimal 2000 karakter.")]
    public string? Catatan { get; init; }

    [MaxLength(100, ErrorMessage = "Maksimal 100 unit per pengembalian.")]
    public IReadOnlyCollection<KondisiUnitRequest> Items { get; init; } = [];
}

public sealed class KondisiUnitRequest
{
    [Range(1, uint.MaxValue, ErrorMessage = "ID unit alat tidak valid.")]
    public uint IdUnitAlat { get; init; }

    [Required(ErrorMessage = "Status kondisi wajib diisi.")]
    [MaxLength(30, ErrorMessage = "Status kondisi maksimal 30 karakter.")]
    public string StatusKondisi { get; init; } = "baik";

    [MaxLength(2000, ErrorMessage = "Catatan unit maksimal 2000 karakter.")]
    public string? Catatan { get; init; }
}

public sealed class VerifikasiPengembalianRequest
{
    public DateOnly? TanggalKembali { get; init; }

    [MaxLength(2000, ErrorMessage = "Catatan maksimal 2000 karakter.")]
    public string? Catatan { get; init; }

    [Required(ErrorMessage = "Kondisi unit wajib diisi.")]
    [MinLength(1, ErrorMessage = "Minimal satu kondisi unit harus diisi.")]
    [MaxLength(100, ErrorMessage = "Maksimal 100 unit per verifikasi.")]
    public IReadOnlyCollection<KondisiUnitRequest> Items { get; init; } = [];
}

public sealed record DetailPengembalianResponse(
    uint Id,
    uint IdUnitAlat,
    string KodeInventaris,
    string NamaAlat,
    string StatusKondisi,
    decimal DendaKondisi,
    string? Catatan);

public sealed record PengembalianResponse(
    uint Id,
    uint IdPeminjaman,
    uint IdPeminjam,
    string UsernamePeminjam,
    string NamaPeminjam,
    uint? IdPetugas,
    string? NamaPetugas,
    string StatusPeminjaman,
    DateOnly TanggalPeminjaman,
    DateOnly TanggalRencanaKembali,
    DateOnly TanggalKembali,
    int KeterlambatanPengembalian,
    decimal Denda,
    string? Catatan);

public sealed record PengembalianDetailResponse(
    uint Id,
    uint IdPeminjaman,
    uint IdPeminjam,
    string UsernamePeminjam,
    string NamaPeminjam,
    uint? IdPetugas,
    string? NamaPetugas,
    string StatusPeminjaman,
    DateOnly TanggalPeminjaman,
    DateOnly TanggalRencanaKembali,
    DateOnly TanggalKembali,
    int KeterlambatanPengembalian,
    decimal Denda,
    string? Catatan,
    IReadOnlyCollection<DetailPengembalianResponse> Items);
