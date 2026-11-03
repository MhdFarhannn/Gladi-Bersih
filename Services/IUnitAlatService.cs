using Gladi_Bersih.Common;
using Gladi_Bersih.DTOs;

namespace Gladi_Bersih.Services;

public interface IUnitAlatService
{
    Task<PagedResult<UnitAlatResponse>> GetAllAsync(int page, int pageSize, string? search, CancellationToken cancellationToken);
    Task<UnitAlatResponse> GetByIdAsync(uint id, CancellationToken cancellationToken);
    Task<UnitAlatResponse> CreateAsync(uint actorId, UnitAlatCreateRequest request, CancellationToken cancellationToken);
    Task<UnitAlatResponse> UpdateAsync(uint actorId, uint id, UnitAlatUpdateRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(uint actorId, uint id, CancellationToken cancellationToken);
}
