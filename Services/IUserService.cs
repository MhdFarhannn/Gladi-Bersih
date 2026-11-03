using Gladi_Bersih.Common;
using Gladi_Bersih.DTOs;

namespace Gladi_Bersih.Services;

public interface IUserService
{
    Task<PagedResult<UserResponse>> GetAllAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken);

    Task<UserResponse> GetByIdAsync(uint id, CancellationToken cancellationToken);
    Task<UserResponse> CreateAsync(uint actorId, UserCreateRequest request, CancellationToken cancellationToken);
    Task<UserResponse> UpdateAsync(uint actorId, uint id, UserUpdateRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(uint actorId, uint id, CancellationToken cancellationToken);
}
