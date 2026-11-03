using System.ComponentModel.DataAnnotations;

namespace Gladi_Bersih.DTOs;

public sealed class UserCreateRequest
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

    [Required(ErrorMessage = "Role wajib diisi.")]
    public string Role { get; init; } = string.Empty;
}

public sealed class UserUpdateRequest
{
    [Required(ErrorMessage = "Username wajib diisi.")]
    [MaxLength(50, ErrorMessage = "Username maksimal 50 karakter.")]
    public string Username { get; init; } = string.Empty;

    [Required(ErrorMessage = "Nama wajib diisi.")]
    [MaxLength(100, ErrorMessage = "Nama maksimal 100 karakter.")]
    public string Nama { get; init; } = string.Empty;

    [MinLength(8, ErrorMessage = "Password minimal 8 karakter.")]
    public string? Password { get; init; }

    [Required(ErrorMessage = "Nomor telepon wajib diisi.")]
    [MaxLength(30, ErrorMessage = "Nomor telepon terlalu panjang.")]
    public string NoTelepon { get; init; } = string.Empty;

    public string? Alamat { get; init; }

    [Required(ErrorMessage = "Role wajib diisi.")]
    public string Role { get; init; } = string.Empty;
}
