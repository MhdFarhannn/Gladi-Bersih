using Gladi_Bersih.DTOs;

namespace Gladi_Bersih.Services;

public interface IBootstrapService
{
    Task<UserResponse> CreateFirstAdminAsync(
        BootstrapAdminRequest request,
        CancellationToken cancellationToken);
}
