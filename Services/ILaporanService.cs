using Gladi_Bersih.DTOs;

namespace Gladi_Bersih.Services;

public interface ILaporanService
{
    Task<LaporanPeminjamanResponse> GetPeminjamanAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);

    Task<LaporanPengembalianResponse> GetPengembalianAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);

    byte[] CreatePeminjamanCsv(LaporanPeminjamanResponse report);
    byte[] CreatePengembalianCsv(LaporanPengembalianResponse report);
}
