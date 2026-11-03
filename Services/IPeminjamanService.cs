using Gladi_Bersih.Common;
using Gladi_Bersih.DTOs;

namespace Gladi_Bersih.Services;

public interface IPeminjamanService
{
    Task<PagedResult<PeminjamanResponse>> GetAllAsync(
        uint actorId,
        string actorRole,
        int page,
        int pageSize,
        string? search,
        string? status,
        CancellationToken cancellationToken);

    Task<PeminjamanDetailResponse> GetByIdAsync(
        uint actorId,
        string actorRole,
        uint id,
        CancellationToken cancellationToken);

    Task<PeminjamanDetailResponse> CreateAsync(
        uint actorId,
        string actorRole,
        PeminjamanRequest request,
        CancellationToken cancellationToken);

    Task<PeminjamanDetailResponse> UpdateAsync(
        uint actorId,
        string actorRole,
        uint id,
        PeminjamanRequest request,
        CancellationToken cancellationToken);

    Task<PeminjamanDetailResponse> ApproveAsync(uint officerId, uint id, CancellationToken cancellationToken);
    Task<PeminjamanDetailResponse> RejectAsync(uint officerId, uint id, string? reason, CancellationToken cancellationToken);
    Task<PeminjamanDetailResponse> HandOverAsync(uint officerId, uint id, CancellationToken cancellationToken);
    Task DeleteAsync(uint actorId, uint id, CancellationToken cancellationToken);
}
