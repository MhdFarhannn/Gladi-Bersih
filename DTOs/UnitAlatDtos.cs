using System.ComponentModel.DataAnnotations;

namespace Gladi_Bersih.DTOs;

public sealed class UnitAlatCreateRequest
{
    [Range(1, uint.MaxValue, ErrorMessage = "Alat wajib dipilih.")]
    public uint IdAlat { get; init; }

    [Required(ErrorMessage = "Kode inventaris wajib diisi.")]
    [MaxLength(50, ErrorMessage = "Kode inventaris maksimal 50 karakter.")]
    public string KodeInventaris { get; init; } = string.Empty;

    public string StatusKetersediaan { get; init; } = "tersedia";
    public string StatusBarang { get; init; } = "baik";
}

public sealed class UnitAlatUpdateRequest
{
    [Range(1, uint.MaxValue, ErrorMessage = "Alat wajib dipilih.")]
    public uint IdAlat { get; init; }

    [Required(ErrorMessage = "Kode inventaris wajib diisi.")]
    [MaxLength(50, ErrorMessage = "Kode inventaris maksimal 50 karakter.")]
    public string KodeInventaris { get; init; } = string.Empty;

    [Required(ErrorMessage = "Status ketersediaan wajib diisi.")]
    public string StatusKetersediaan { get; init; } = string.Empty;

    [Required(ErrorMessage = "Status barang wajib diisi.")]
    public string StatusBarang { get; init; } = string.Empty;
}

public sealed record UnitAlatResponse(
    uint Id,
    uint IdAlat,
    string Alat,
    string KodeInventaris,
    string StatusKetersediaan,
    string StatusBarang,
    DateTime CreatedAt);
