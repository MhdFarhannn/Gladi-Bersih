namespace Gladi_Bersih.Models;

public sealed class PeminjamanModel
{
    public uint Id { get; init; }
    public uint IdPeminjam { get; init; }
    public string UsernamePeminjam { get; init; } = string.Empty;
    public string NamaPeminjam { get; init; } = string.Empty;
    public uint? IdPetugas { get; init; }
    public string? NamaPetugas { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateOnly TanggalPeminjaman { get; init; }
    public DateOnly TanggalRencanaKembali { get; init; }
    public decimal TotalBiaya { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class DetailPeminjamanModel
{
    public uint Id { get; init; }
    public uint IdAlat { get; init; }
    public string NamaAlat { get; init; } = string.Empty;
    public int Jumlah { get; init; }
    public decimal HargaSewaSaatTransaksi { get; init; }
}

public sealed class UnitPeminjamanModel
{
    public uint IdDetailPeminjaman { get; init; }
    public uint IdUnitAlat { get; init; }
    public string KodeInventaris { get; init; } = string.Empty;
}

public sealed class KetersediaanAlatModel
{
    public uint Id { get; init; }
    public string Nama { get; init; } = string.Empty;
    public decimal HargaSewa { get; init; }
    public int StokTersedia { get; init; }
}
