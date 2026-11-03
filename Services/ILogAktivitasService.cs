using Gladi_Bersih.Common;
using Gladi_Bersih.DTOs;

namespace Gladi_Bersih.Services;

public interface ILogAktivitasService
{
    Task<PagedResult<LogAktivitasResponse>> GetAllAsync(
        int page,
        int pageSize,
        string? search,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken);
}
