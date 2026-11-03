using System.ComponentModel.DataAnnotations;

namespace Gladi_Bersih.DTOs;

public sealed class LoginRequest
{
    [Required(ErrorMessage = "Username wajib diisi.")]
    [MaxLength(50, ErrorMessage = "Username maksimal 50 karakter.")]
    public string Username { get; init; } = string.Empty;

    [Required(ErrorMessage = "Password wajib diisi.")]
    public string Password { get; init; } = string.Empty;
}

public class RegisterRequest
{
    [Required(ErrorMessage = "Username wajib diisi.")]
    [MaxLength(50, ErrorMessage = "Username maksimal 50 karakter.")]
    public string Username { get; init; } = string.Empty;

    [Required(ErrorMessage = "Nama wajib diisi.")]
    [MaxLength(100, ErrorMessage = "Nama maksimal 100 karakter.")]
    public string Nama { get; init; } = string.Empty;

    [Required(ErrorMessage = "Password wajib diisi.")]
    [MinLength(8, ErrorMessage = "Password minimal 8 karakter.")]
    public string Password { get; init; } = string.Empty;

    [Required(ErrorMessage = "Nomor telepon wajib diisi.")]
    [MaxLength(30, ErrorMessage = "Nomor telepon terlalu panjang.")]
    public string NoTelepon { get; init; } = string.Empty;

    public string? Alamat { get; init; }
}

public sealed class BootstrapAdminRequest : RegisterRequest
{
}

public sealed record AuthResponse(
    string Token,
    DateTime ExpiresAt,
    UserResponse User);

public sealed record UserResponse(
    uint Id,
    string Username,
    string Nama,
    string NoTelepon,
    string? Alamat,
    string Role,
    DateTime CreatedAt);
