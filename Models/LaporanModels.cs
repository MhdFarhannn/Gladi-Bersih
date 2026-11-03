namespace Gladi_Bersih.Models;

public sealed class LaporanPeminjamanModel
{
    public uint IdPeminjaman { get; init; }
    public DateOnly TanggalPeminjaman { get; init; }
    public DateOnly TanggalRencanaKembali { get; init; }
    public string UsernamePeminjam { get; init; } = string.Empty;
    public string NamaPeminjam { get; init; } = string.Empty;
    public string? NamaPetugas { get; init; }
    public string StatusPeminjaman { get; init; } = string.Empty;
    public string NamaKategori { get; init; } = string.Empty;
    public string NamaAlat { get; init; } = string.Empty;
    public int Jumlah { get; init; }
    public decimal HargaSewaSaatTransaksi { get; init; }
    public decimal SubtotalPerHari { get; init; }
    public decimal TotalBiaya { get; init; }
    public string? KodeUnit { get; init; }
    public DateOnly? TanggalKembali { get; init; }
    public int? KeterlambatanPengembalian { get; init; }
    public decimal? Denda { get; init; }
}

public sealed class LaporanPengembalianModel
{
    public uint IdPengembalian { get; init; }
    public uint IdPeminjaman { get; init; }
    public string UsernamePeminjam { get; init; } = string.Empty;
    public string NamaPeminjam { get; init; } = string.Empty;
    public string? NamaPetugas { get; init; }
    public DateOnly TanggalRencanaKembali { get; init; }
    public DateOnly TanggalKembali { get; init; }
    public int KeterlambatanPengembalian { get; init; }
    public decimal DendaTotal { get; init; }
    public string NamaAlat { get; init; } = string.Empty;
    public string KodeInventaris { get; init; } = string.Empty;
    public string StatusKondisi { get; init; } = string.Empty;
    public decimal DendaKondisi { get; init; }
    public string? CatatanUnit { get; init; }
}
