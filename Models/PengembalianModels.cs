namespace Gladi_Bersih.Models;

public sealed class PengembalianModel
{
    public uint Id { get; init; }
    public uint IdPeminjaman { get; init; }
    public uint IdPeminjam { get; init; }
    public string UsernamePeminjam { get; init; } = string.Empty;
    public string NamaPeminjam { get; init; } = string.Empty;
    public uint? IdPetugas { get; init; }
    public string? NamaPetugas { get; init; }
    public string StatusPeminjaman { get; init; } = string.Empty;
    public DateOnly TanggalPeminjaman { get; init; }
    public DateOnly TanggalRencanaKembali { get; init; }
    public DateOnly TanggalKembali { get; init; }
    public int KeterlambatanPengembalian { get; init; }
    public decimal Denda { get; init; }
    public string? Catatan { get; init; }
}

public sealed class DetailPengembalianModel
{
    public uint Id { get; init; }
    public uint IdUnitAlat { get; init; }
    public string KodeInventaris { get; init; } = string.Empty;
    public string NamaAlat { get; init; } = string.Empty;
    public string StatusKondisi { get; init; } = string.Empty;
    public decimal DendaKondisi { get; init; }
    public string? Catatan { get; init; }
    public decimal HargaSewaSaatTransaksi { get; init; }
}

public sealed class UnitUntukPengembalianModel
{
    public uint IdUnitAlat { get; init; }
    public string KodeInventaris { get; init; } = string.Empty;
    public string NamaAlat { get; init; } = string.Empty;
    public decimal HargaSewaSaatTransaksi { get; init; }
}

public sealed class PeminjamanUntukPengembalianModel
{
    public uint Id { get; init; }
    public uint IdPeminjam { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateOnly TanggalPeminjaman { get; init; }
    public DateOnly TanggalRencanaKembali { get; init; }
}
