using Gladi_Bersih.Common;
using Gladi_Bersih.DTOs;

namespace Gladi_Bersih.Services;

public interface IKategoriService
{
    Task<PagedResult<KategoriResponse>> GetAllAsync(int page, int pageSize, string? search, CancellationToken cancellationToken);
    Task<KategoriResponse> GetByIdAsync(uint id, CancellationToken cancellationToken);
    Task<KategoriResponse> CreateAsync(uint actorId, KategoriRequest request, CancellationToken cancellationToken);
    Task<KategoriResponse> UpdateAsync(uint actorId, uint id, KategoriRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(uint actorId, uint id, CancellationToken cancellationToken);
}
