using System.ComponentModel.DataAnnotations;

namespace Gladi_Bersih.DTOs;

public sealed class KategoriRequest
{
    [Required(ErrorMessage = "Nama kategori wajib diisi.")]
    [MaxLength(100, ErrorMessage = "Nama kategori maksimal 100 karakter.")]
    public string Nama { get; init; } = string.Empty;
}

public sealed record KategoriResponse(uint Id, string Nama);
