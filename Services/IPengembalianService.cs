using Gladi_Bersih.Common;
using Gladi_Bersih.DTOs;

namespace Gladi_Bersih.Services;

public interface IPengembalianService
{
    Task<PagedResult<PengembalianResponse>> GetAllAsync(
        uint actorId,
        string actorRole,
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken);

    Task<PengembalianDetailResponse> GetByIdAsync(
        uint actorId,
        string actorRole,
        uint id,
        CancellationToken cancellationToken);

    Task<PengembalianDetailResponse> CreateAsync(
        uint actorId,
        string actorRole,
        PengembalianRequest request,
        CancellationToken cancellationToken);

    Task<PengembalianDetailResponse> VerifyAsync(
        uint actorId,
        uint id,
        VerifikasiPengembalianRequest request,
        CancellationToken cancellationToken);

    Task DeleteAsync(uint actorId, uint id, CancellationToken cancellationToken);
}
