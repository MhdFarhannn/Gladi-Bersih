using Gladi_Bersih.DTOs;

namespace Gladi_Bersih.Services;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task LogoutAsync(uint userId, CancellationToken cancellationToken);
}
