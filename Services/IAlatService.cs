using Gladi_Bersih.Common;
using Gladi_Bersih.DTOs;

namespace Gladi_Bersih.Services;

public interface IAlatService
{
    Task<PagedResult<AlatResponse>> GetAllAsync(int page, int pageSize, string? search, CancellationToken cancellationToken);
    Task<AlatResponse> GetByIdAsync(uint id, CancellationToken cancellationToken);
    Task<AlatResponse> CreateAsync(uint actorId, AlatRequest request, CancellationToken cancellationToken);
    Task<AlatResponse> UpdateAsync(uint actorId, uint id, AlatRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(uint actorId, uint id, CancellationToken cancellationToken);
}
