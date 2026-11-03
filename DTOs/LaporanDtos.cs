namespace Gladi_Bersih.DTOs;

public sealed record LaporanPeminjamanRow(
    uint IdPeminjaman,
    DateOnly TanggalPeminjaman,
    DateOnly TanggalRencanaKembali,
    string UsernamePeminjam,
    string NamaPeminjam,
    string? NamaPetugas,
    string StatusPeminjaman,
    string NamaKategori,
    string NamaAlat,
    int Jumlah,
    decimal HargaSewaSaatTransaksi,
    decimal SubtotalPerHari,
    decimal TotalBiaya,
    string? KodeUnit,
    DateOnly? TanggalKembali,
    int? KeterlambatanPengembalian,
    decimal? Denda);

public sealed record RingkasanPeminjaman(int JumlahPeminjaman, int JumlahUnit, decimal TotalBiaya);

public sealed record LaporanPeminjamanResponse(
    DateOnly Dari,
    DateOnly Sampai,
    DateTime DibuatPada,
    RingkasanPeminjaman Ringkasan,
    IReadOnlyCollection<LaporanPeminjamanRow> Items);

public sealed record LaporanPengembalianRow(
    uint IdPengembalian,
    uint IdPeminjaman,
    string UsernamePeminjam,
    string NamaPeminjam,
    string? NamaPetugas,
    DateOnly TanggalRencanaKembali,
    DateOnly TanggalKembali,
    int KeterlambatanPengembalian,
    decimal DendaTotal,
    string NamaAlat,
    string KodeInventaris,
    string StatusKondisi,
    decimal DendaKondisi,
    string? CatatanUnit);

public sealed record RingkasanPengembalian(int JumlahPengembalian, int JumlahUnit, decimal TotalDenda);

public sealed record LaporanPengembalianResponse(
    DateOnly Dari,
    DateOnly Sampai,
    DateTime DibuatPada,
    RingkasanPengembalian Ringkasan,
    IReadOnlyCollection<LaporanPengembalianRow> Items);
