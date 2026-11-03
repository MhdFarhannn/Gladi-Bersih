using System.ComponentModel.DataAnnotations;

namespace Gladi_Bersih.DTOs;

public sealed class PeminjamanRequest
{
    public uint? IdPeminjam { get; init; }

    [Required(ErrorMessage = "Tanggal peminjaman wajib diisi.")]
    public DateOnly TanggalPeminjaman { get; init; }

    [Required(ErrorMessage = "Tanggal rencana kembali wajib diisi.")]
    public DateOnly TanggalRencanaKembali { get; init; }

    [Required(ErrorMessage = "Detail alat wajib diisi.")]
    [MinLength(1, ErrorMessage = "Minimal satu alat harus dipinjam.")]
    [MaxLength(100, ErrorMessage = "Maksimal 100 jenis alat per peminjaman.")]
    public IReadOnlyCollection<PeminjamanItemRequest> Items { get; init; } = [];
}

public sealed class PeminjamanItemRequest
{
    [Range(1, uint.MaxValue, ErrorMessage = "ID alat tidak valid.")]
    public uint IdAlat { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "Jumlah alat harus lebih dari 0.")]
    public int Jumlah { get; init; }
}

public sealed class PenolakanPeminjamanRequest
{
    [MaxLength(500, ErrorMessage = "Alasan maksimal 500 karakter.")]
    public string? Alasan { get; init; }
}

public sealed record UnitPeminjamanResponse(uint IdUnitAlat, string KodeInventaris);

public sealed record DetailPeminjamanResponse(
    uint Id,
    uint IdAlat,
    string NamaAlat,
    int Jumlah,
    decimal HargaSewaSaatTransaksi,
    IReadOnlyCollection<UnitPeminjamanResponse> Unit);

public sealed record PeminjamanResponse(
    uint Id,
    uint IdPeminjam,
    string UsernamePeminjam,
    string NamaPeminjam,
    uint? IdPetugas,
    string? NamaPetugas,
    string Status,
    DateOnly TanggalPeminjaman,
    DateOnly TanggalRencanaKembali,
    decimal TotalBiaya,
    DateTime CreatedAt);

public sealed record PeminjamanDetailResponse(
    uint Id,
    uint IdPeminjam,
    string UsernamePeminjam,
    string NamaPeminjam,
    uint? IdPetugas,
    string? NamaPetugas,
    string Status,
    DateOnly TanggalPeminjaman,
    DateOnly TanggalRencanaKembali,
    decimal TotalBiaya,
    DateTime CreatedAt,
    IReadOnlyCollection<DetailPeminjamanResponse> Items);
