using Gladi_Bersih.DTOs;
using Gladi_Bersih.Models;

namespace Gladi_Bersih.Services;

public interface IJwtTokenService
{
    AuthResponse CreateToken(UserModel user);
}
